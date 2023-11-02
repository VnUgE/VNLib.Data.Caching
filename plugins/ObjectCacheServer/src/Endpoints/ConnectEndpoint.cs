/*
* Copyright (c) 2023 Vaughn Nugent
* 
* Library: VNLib
* Package: ObjectCacheServer
* File: ConnectEndpoint.cs 
*
* ConnectEndpoint.cs is part of ObjectCacheServer which is part of the larger 
* VNLib collection of libraries and utilities.
*
* ObjectCacheServer is free software: you can redistribute it and/or modify 
* it under the terms of the GNU Affero General Public License as 
* published by the Free Software Foundation, either version 3 of the
* License, or (at your option) any later version.
*
* ObjectCacheServer is distributed in the hope that it will be useful,
* but WITHOUT ANY WARRANTY; without even the implied warranty of
* MERCHANTABILITY or FITNESS FOR A PARTICULAR PURPOSE. See the
* GNU Affero General Public License for more details.
*
* You should have received a copy of the GNU Affero General Public License
* along with this program.  If not, see https://www.gnu.org/licenses/.
*/

using System;
using System.Net;
using System.Threading;
using System.Threading.Tasks;
using System.Collections.Generic;

using VNLib.Net.Http;
using VNLib.Utils.Memory;
using VNLib.Utils.Logging;
using VNLib.Data.Caching;
using VNLib.Data.Caching.Extensions;
using VNLib.Hashing.IdentityUtility;
using VNLib.Net.Messaging.FBM;
using VNLib.Net.Messaging.FBM.Client;
using VNLib.Net.Messaging.FBM.Server;
using VNLib.Plugins;
using VNLib.Plugins.Essentials;
using VNLib.Plugins.Essentials.Endpoints;
using VNLib.Plugins.Essentials.Extensions;
using VNLib.Plugins.Extensions.Loading;
using VNLib.Plugins.Extensions.Loading.Routing;
using VNLib.Data.Caching.Extensions.Clustering;
using VNLib.Data.Caching.ObjectCache.Server.Cache;
using VNLib.Data.Caching.ObjectCache.Server.Clustering;


namespace VNLib.Data.Caching.ObjectCache.Server.Endpoints
{

    internal sealed class ConnectEndpoint : ResourceEndpointBase
    {
        internal const string LOG_SCOPE_NAME = "CONEP";

       
        private readonly ICacheEventQueueManager PubSubManager;
        private readonly IPeerMonitor Peers;
        private readonly BlobCacheListener<IPeerEventQueue> Store;
        private readonly NodeConfig NodeConfiguration;
        private readonly CacheNegotationManager AuthManager;

        private uint _connectedClients;

        /// <summary>
        /// Gets the number of active connections 
        /// </summary>
        public uint ConnectedClients => _connectedClients;

        /// <summary>
        /// The cache store configuration
        /// </summary>
        public CacheConfiguration CacheConfig { get; }

        //Loosen up protection settings
        ///<inheritdoc/>
        protected override ProtectionSettings EndpointProtectionSettings { get; } = new()
        {
            DisableSessionsRequired = true
        };

        public ConnectEndpoint(PluginBase plugin)
        {
            //Get node configuration
            NodeConfiguration = plugin.GetOrCreateSingleton<NodeConfig>();

            //Init from config and create a new log scope
            InitPathAndLog(NodeConfiguration.ConnectPath, plugin.Log.CreateScope(LOG_SCOPE_NAME));

            //Setup pub/sub manager
            PubSubManager = plugin.GetOrCreateSingleton<CacheEventQueueManager>();

            //Get peer monitor
            Peers = plugin.GetOrCreateSingleton<CachePeerMonitor>();

            //Init the cache store
            Store = plugin.GetOrCreateSingleton<CacheStore>().Listener;

            //Get the cache store configuration
            CacheConfig = plugin.GetConfigForType<CacheStore>().Deserialze<CacheConfiguration>();

            //Get the auth manager
            AuthManager = plugin.GetOrCreateSingleton<CacheNegotationManager>();
        }
      

        /*
         * Used as a client negotiation and verification request
         * 
         * The token created during this request will be verified by the client
         * and is already verified by this server, will be passed back 
         * via the authorization header during the websocket upgrade.
         * 
         * This server must verify the authenticity of the returned token
         * 
         * The tokens are very short lived as requests are intended to be made
         * directly after verification
         * 
         * Clients must also sign the entire token with their private key and 
         * set the signature in the x-upgrade-sig header so we can verify they
         * received the messages properly
         */

        protected override VfReturnType Get(HttpEntity entity)
        {
            //Parse jwt from authoriation
            string? jwtAuth = entity.Server.Headers[HttpRequestHeader.Authorization];
            if (string.IsNullOrWhiteSpace(jwtAuth))
            {
                return VirtualClose(entity, HttpStatusCode.Forbidden);
            }

            //Create negotiation state
            if(!AuthManager.IsClientNegotiationValid(jwtAuth, out ClientNegotiationState state))
            {
                Log.Information("Initial negotiation client signature verification failed");
                return VirtualClose(entity, HttpStatusCode.Unauthorized);
            }

            if (state.IsPeer)
            {
                Log.Debug("Received negotiation request from peer node {node}", state.NodeId);
            }
            else
            {
                Log.Debug("Received negotiation request from client {client}", entity.TrustedRemoteIp.ToString());
            }

            //Verified, now we can create an auth message with a short expiration
            using JsonWebToken auth = AuthManager.ConfirmCLientNegotiation(state, entity.TrustedRemoteIp, entity.RequestedTimeUtc);

            //Close response
            entity.CloseResponse(HttpStatusCode.OK, ContentType.Text, auth.DataBuffer);
            return VfReturnType.VirtualSkip;
        }

        protected override VfReturnType WebsocketRequested(HttpEntity entity)
        {
            //Parse jwt from authorization
            string? jwtAuth = entity.Server.Headers[HttpRequestHeader.Authorization];
            string? clientSignature = entity.Server.Headers[FBMDataCacheExtensions.X_UPGRADE_SIG_HEADER];
            string? optionalDiscovery = entity.Server.Headers[FBMDataCacheExtensions.X_NODE_DISCOVERY_HEADER];

            //Not null
            if (string.IsNullOrWhiteSpace(jwtAuth) || string.IsNullOrWhiteSpace(clientSignature))
            {
                return VfReturnType.Forbidden;
            }

            string? nodeId = null;
            bool isPeer = false;

            //Validate upgrade request
            if (!AuthManager.ValidateUpgrade(jwtAuth, clientSignature, entity.RequestedTimeUtc, entity.TrustedRemoteIp, ref nodeId, ref isPeer))
            {
                return VirtualClose(entity, HttpStatusCode.Unauthorized);
            }

            CacheNodeAdvertisment? discoveryAd = null;

            /*
             * If the client is a peer server, it may offer a signed advertisment 
             * that this node will have the duty of making available to other peers
             * if it is valid
             */

            if (isPeer && !string.IsNullOrWhiteSpace(optionalDiscovery))
            {
                discoveryAd = NodeConfiguration.KeyStore.VerifyPeerAdvertisment(optionalDiscovery);
            }

            WsUserState state;

            try
            {               
                //Get query config suggestions from the client
                string recvBufCmd = entity.QueryArgs[FBMClient.REQ_RECV_BUF_QUERY_ARG];
                string maxHeaderCharCmd = entity.QueryArgs[FBMClient.REQ_HEAD_BUF_QUERY_ARG];
                string maxMessageSizeCmd = entity.QueryArgs[FBMClient.REQ_MAX_MESS_QUERY_ARG];
                
                //Parse recv buffer size
                int recvBufSize = int.TryParse(recvBufCmd, out int rbs) ? rbs : CacheConfig.MinRecvBufferSize;
                int maxHeadBufSize = int.TryParse(maxHeaderCharCmd, out int hbs) ? hbs : CacheConfig.MinHeaderBufferSize;
                int maxMessageSize = int.TryParse(maxMessageSizeCmd, out int mxs) ? mxs : CacheConfig.MaxMessageSize;

                /*
                 * Buffer sizing can get messy as the response/resquest sizes can vary
                 * and will include headers, this is a drawback of the FBM protocol 
                 * so we need to properly calculate efficient buffer sizes as 
                 * negotiated with the client.
                 */

                int maxMessageSizeClamp = Math.Clamp(maxMessageSize, CacheConfig.MinRecvBufferSize, CacheConfig.MaxRecvBufferSize);

                //Init new ws state object and clamp the suggested buffer sizes
                state = new()
                {
                    RecvBufferSize = Math.Clamp(recvBufSize, CacheConfig.MinRecvBufferSize, CacheConfig.MaxRecvBufferSize),
                    MaxHeaderBufferSize = Math.Clamp(maxHeadBufSize, CacheConfig.MinHeaderBufferSize, CacheConfig.MaxHeaderBufferSize),

                    MaxMessageSize = maxMessageSizeClamp,

                    /*
                     * Response buffer needs to be large enough to store a max message 
                     * as a response along with all response headers
                     */
                    MaxResponseBufferSize = (int)MemoryUtil.NearestPage(maxMessageSizeClamp),

                    NodeId = nodeId,
                    Advertisment = discoveryAd
                };
            }
            catch (KeyNotFoundException)
            {
                return VfReturnType.BadRequest;
            }

            //Print state message to console
            Log.Debug("Client buffer state {state}", state);

            //Accept socket and pass state object
            _ = entity.AcceptWebSocket(WebsocketAcceptedAsync, state);
            return VfReturnType.VirtualSkip;
        }
        
        private async Task WebsocketAcceptedAsync(WebSocketSession<WsUserState> wss)
        {
            WsUserState state = wss.UserState!;

            //Notify peers of new connection
            Peers.OnPeerConnected(state);

            //Register plugin exit token to cancel the connected socket
            CancellationTokenRegistration reg = this.GetPlugin().UnloadToken.Register(wss.CancelAll);

            //Inc connected count
            Interlocked.Increment(ref _connectedClients);

            try
            {
                //Init listener args from request
                FBMListenerSessionParams args = new()
                {
                    MaxMessageSize = state.MaxMessageSize,
                    RecvBufferSize = state.RecvBufferSize,
                    ResponseBufferSize = state.MaxResponseBufferSize,
                    MaxHeaderBufferSize = state.MaxHeaderBufferSize,

                    HeaderEncoding = Helpers.DefaultEncoding,
                };

                //Check if the client is a peer node, if it is, subscribe to change events
                if (state.IsPeer)
                {
                    //Get the event queue for the current node
                    IPeerEventQueue queue = PubSubManager.Subscribe(state);

                    try
                    {
                        //Begin listening for messages with a queue
                        await Store.ListenAsync(wss, queue, args);
                    }
                    finally
                    {
                        //ALAWYS Detatch listener
                        PubSubManager.Unsubscribe(state);
                    }
                }
                else
                {
                    //Begin listening for messages without a queue
                    await Store.ListenAsync(wss, null!, args);
                }
            }
            catch (OperationCanceledException)
            {
                Log.Debug("Websocket connection was canceled");

                //Disconnect the socket
                await wss.CloseSocketOutputAsync(System.Net.WebSockets.WebSocketCloseStatus.NormalClosure, "unload", CancellationToken.None);
            }
            catch (Exception ex)
            {
                Log.Debug(ex);
            }

            //Dec connected count
            Interlocked.Decrement(ref _connectedClients);

            //Unregister the token
            reg.Unregister();

            //Notify monitor of disconnect
            Peers.OnPeerDisconnected(state);

            Log.Debug("Server websocket exited");
        }
       

        private class WsUserState : ICachePeer
        {
            public int RecvBufferSize { get; init; }
            public int MaxHeaderBufferSize { get; init; }
            public int MaxMessageSize { get; init; }
            public int MaxResponseBufferSize { get; init; }
            public string? NodeId { get; init; }
            public CacheNodeAdvertisment? Advertisment { get; init; }

            public bool IsPeer => !string.IsNullOrWhiteSpace(NodeId);

            public override string ToString()
            {
                return
              $"{nameof(RecvBufferSize)}:{RecvBufferSize}, {nameof(MaxHeaderBufferSize)}: {MaxHeaderBufferSize}, " +
              $"{nameof(MaxMessageSize)}:{MaxMessageSize}, {nameof(MaxResponseBufferSize)}:{MaxResponseBufferSize}";
            }
        }
    }
}
