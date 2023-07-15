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
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using System.Collections.Generic;

using VNLib.Hashing;
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
using VNLib.Plugins.Extensions.Loading;
using VNLib.Plugins.Essentials.Endpoints;
using VNLib.Plugins.Essentials.Extensions;
using VNLib.Plugins.Extensions.Loading.Routing;
using VNLib.Data.Caching.Extensions.Clustering;
using VNLib.Data.Caching.ObjectCache.Server.Cache;
using VNLib.Data.Caching.ObjectCache.Server.Clustering;

namespace VNLib.Data.Caching.ObjectCache.Server.Endpoints
{

    internal sealed class ConnectEndpoint : ResourceEndpointBase
    {
        private const string LOG_SCOPE_NAME = "CONEP";

        private static readonly TimeSpan AuthTokenExpiration = TimeSpan.FromSeconds(30);
      

        private readonly NodeConfig NodeConfiguration;
        private readonly ICacheEventQueueManager PubSubManager;
        private readonly IPeerMonitor Peers;
        private readonly BlobCacheListener Store;
        
        private readonly string AudienceLocalServerId;

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
            DisableBrowsersOnly = true,
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

            /*
            * Generate a random guid for the current server when created so we 
            * know client tokens belong to us when singed by the same key
            */
            AudienceLocalServerId = Guid.NewGuid().ToString("N");
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
                entity.CloseResponse(HttpStatusCode.Unauthorized);
                return VfReturnType.VirtualSkip;
            }

            string? nodeId = null;
            string? challenge = null;
            bool isPeer = false;

            // Parse jwt
            using (JsonWebToken jwt = JsonWebToken.Parse(jwtAuth))
            {
                //verify signature for client
                if (NodeConfiguration.KeyStore.VerifyJwt(jwt, false))
                {
                    //Validated
                }
                //May be signed by a cache server
                else if(NodeConfiguration.KeyStore.VerifyJwt(jwt, true))
                {
                    //Set peer and verified flag since the another cache server signed the request
                    isPeer = true;
                }
                else
                {
                    Log.Information("Client signature verification failed");
                    entity.CloseResponse(HttpStatusCode.Unauthorized);
                    return VfReturnType.VirtualSkip;
                }

                //Recover json body
                using JsonDocument doc = jwt.GetPayload();

                if (doc.RootElement.TryGetProperty("sub", out JsonElement servIdEl))
                {
                    nodeId = servIdEl.GetString();
                }

                if (doc.RootElement.TryGetProperty("chl", out JsonElement challengeEl))
                {
                    challenge = challengeEl.GetString();
                }
            }

            if (isPeer)
            {
                Log.Debug("Received negotiation request from peer node {node}", nodeId);
            }
            else
            {
                Log.Debug("Received negotiation request from client {client}", entity.TrustedRemoteIp.ToString());
            }

            //Verified, now we can create an auth message with a short expiration
            using JsonWebToken auth = new();
            
            auth.WriteHeader(NodeConfiguration.KeyStore.GetJwtHeader());
            auth.InitPayloadClaim()
                .AddClaim("aud", AudienceLocalServerId)
                .AddClaim("iat", entity.RequestedTimeUtc.ToUnixTimeSeconds())
                .AddClaim("exp", entity.RequestedTimeUtc.Add(AuthTokenExpiration).ToUnixTimeSeconds())
                .AddClaim("nonce", RandomHash.GetRandomBase32(8))
                .AddClaim("chl", challenge!)
                //Set the ispeer flag if the request was signed by a cache server
                .AddClaim("isPeer", isPeer)
                //Specify the server's node id if set
                .AddClaim("sub", nodeId!)
                //Set ip address
                .AddClaim("ip", entity.TrustedRemoteIp.ToString())
                //Add negotiaion args
                .AddClaim(FBMClient.REQ_HEAD_BUF_QUERY_ARG, CacheConfig.MaxHeaderBufferSize)
                .AddClaim(FBMClient.REQ_RECV_BUF_QUERY_ARG, CacheConfig.MaxRecvBufferSize)
                .AddClaim(FBMClient.REQ_MAX_MESS_QUERY_ARG, CacheConfig.MaxMessageSize)
                .CommitClaims();

            //Sign the auth message from our private key
            NodeConfiguration.KeyStore.SignJwt(auth);

            //Close response
            entity.CloseResponse(HttpStatusCode.OK, ContentType.Text, auth.DataBuffer);
            return VfReturnType.VirtualSkip;
        }

        protected override VfReturnType WebsocketRequested(HttpEntity entity)
        {
            //Parse jwt from authorization
            string? jwtAuth = entity.Server.Headers[HttpRequestHeader.Authorization];

            if (string.IsNullOrWhiteSpace(jwtAuth))
            {
                entity.CloseResponse(HttpStatusCode.Unauthorized);
                return VfReturnType.VirtualSkip;
            }

            //Get the upgrade signature header
            string? clientSignature = entity.Server.Headers[FBMDataCacheExtensions.X_UPGRADE_SIG_HEADER];

            if (string.IsNullOrWhiteSpace(clientSignature))
            {
                entity.CloseResponse(HttpStatusCode.Unauthorized);
                return VfReturnType.VirtualSkip;
            }

            string? nodeId = null;
            CacheNodeAdvertisment? discoveryAd = null;

            //Parse jwt
            using (JsonWebToken jwt = JsonWebToken.Parse(jwtAuth))
            {
                //verify signature against the cache public key, since this server must have signed it
                if (!NodeConfiguration.KeyStore.VerifyCachePeer(jwt))
                {
                    entity.CloseResponse(HttpStatusCode.Unauthorized);
                    return VfReturnType.VirtualSkip;
                }

                //Recover json body
                using JsonDocument doc = jwt.GetPayload();

                //Verify audience, expiration

                if (!doc.RootElement.TryGetProperty("aud", out JsonElement audEl) 
                    || !AudienceLocalServerId.Equals(audEl.GetString(), StringComparison.OrdinalIgnoreCase))
                {
                    entity.CloseResponse(HttpStatusCode.Unauthorized);
                    return VfReturnType.VirtualSkip;
                }

                if (!doc.RootElement.TryGetProperty("exp", out JsonElement expEl)
                    || DateTimeOffset.FromUnixTimeSeconds(expEl.GetInt64()) < entity.RequestedTimeUtc)
                {
                    entity.CloseResponse(HttpStatusCode.Unauthorized);
                    return VfReturnType.VirtualSkip;
                }

                //Check node ip address matches if required
                if (NodeConfiguration.VerifyIp)
                {
                    if (!doc.RootElement.TryGetProperty("ip", out JsonElement ipEl))
                    {
                        entity.CloseResponse(HttpStatusCode.Unauthorized);
                        return VfReturnType.VirtualSkip;
                    }

                    string? clientIp = ipEl.GetString();
                    //Verify the client ip address matches the one in the token
                    if (clientIp == null || !IPAddress.TryParse(clientIp, out IPAddress? clientIpAddr) || !clientIpAddr.Equals(entity.TrustedRemoteIp))
                    {
                        entity.CloseResponse(HttpStatusCode.Unauthorized);
                        return VfReturnType.VirtualSkip;
                    }
                }

                //Check if the client is a peer
                bool isPeer = doc.RootElement.TryGetProperty("isPeer", out JsonElement isPeerEl) && isPeerEl.GetBoolean();

                //The node id is optional and stored in the 'sub' field, ignore if the client is not a peer
                if (isPeer && doc.RootElement.TryGetProperty("sub", out JsonElement servIdEl))
                {
                    nodeId = servIdEl.GetString();
                }

                //Verify the signature the client included of the auth token

                //Verify token signature against a fellow cache public key
                if (!NodeConfiguration.KeyStore.VerifyUpgradeToken(clientSignature, jwtAuth, isPeer))
                {
                    entity.CloseResponse(HttpStatusCode.Unauthorized);
                    return VfReturnType.VirtualSkip;
                }

                if (isPeer)
                {
                    //Try to get the node advertisement header
                    string? discoveryHeader = entity.Server.Headers[FBMDataCacheExtensions.X_NODE_DISCOVERY_HEADER];

                    //Verify the node advertisement header and publish it
                    if (!string.IsNullOrWhiteSpace(discoveryHeader))
                    {
                        discoveryAd = NodeConfiguration.KeyStore.VerifyPeerAdvertisment(discoveryHeader);
                    }
                }
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
            entity.AcceptWebSocket(WebsocketAcceptedAsync, state);
            return VfReturnType.VirtualSkip;
        }
        
        private async Task WebsocketAcceptedAsync(WebSocketSession wss)
        {
            WsUserState state = (WsUserState)wss.UserState!;

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
                if (!string.IsNullOrWhiteSpace(state.NodeId))
                {
                    //Get the event queue for the current node
                    IPeerEventQueue queue = PubSubManager.Subscribe(state);

                    try
                    {
                        //Begin listening for messages with a queue
                        await Store.ListenAsync(wss, args, queue);
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
                    await Store.ListenAsync(wss, args, null);
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

            public override string ToString()
            {
                return
              $"{nameof(RecvBufferSize)}:{RecvBufferSize}, {nameof(MaxHeaderBufferSize)}: {MaxHeaderBufferSize}, " +
              $"{nameof(MaxMessageSize)}:{MaxMessageSize}, {nameof(MaxResponseBufferSize)}:{MaxResponseBufferSize}";
            }
        }
    }
}
