/*
* Copyright (c) 2022 Vaughn Nugent
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
using System.Threading.Channels;
using System.Collections.Generic;
using System.Collections.Concurrent;

using VNLib.Net.Http;
using VNLib.Hashing;
using VNLib.Utils.Async;
using VNLib.Utils.Logging;
using VNLib.Hashing.IdentityUtility;
using VNLib.Net.Messaging.FBM;
using VNLib.Net.Messaging.FBM.Client;
using VNLib.Net.Messaging.FBM.Server;
using VNLib.Data.Caching.ObjectCache;
using VNLib.Plugins.Extensions.Loading;
using VNLib.Plugins.Essentials.Endpoints;
using VNLib.Plugins.Essentials.Extensions;


namespace VNLib.Plugins.Essentials.Sessions.Server.Endpoints
{
    internal sealed class ConnectEndpoint : ResourceEndpointBase
    {
        const int MAX_RECV_BUF_SIZE = 1000 * 1024;
        const int MIN_RECV_BUF_SIZE = 8 * 1024;
        const int MAX_HEAD_BUF_SIZE = 2048;
        const int MIN_MESSAGE_SIZE = 10 * 1024;
        const int MAX_MESSAGE_SIZE = 1000 * 1024;
        const int MIN_HEAD_BUF_SIZE = 128;
        const int MAX_EVENT_QUEUE_SIZE = 10000;
        const int MAX_RESPONSE_BUFFER_SIZE = 10 * 1024;

        private static readonly TimeSpan AuthTokenExpiration = TimeSpan.FromSeconds(30);

        private readonly string AudienceLocalServerId;
        private readonly ObjectCacheStore Store;
        private readonly PluginBase Pbase;

        private readonly ConcurrentDictionary<string, AsyncQueue<ChangeEvent>> StatefulEventQueue;

        private uint _connectedClients;

        public uint ConnectedClients => _connectedClients;

        //Loosen up protection settings
        protected override ProtectionSettings EndpointProtectionSettings { get; } = new()
        {
            DisableBrowsersOnly = true,
            DisableSessionsRequired = true,
            DisableCrossSiteDenied = true
        };

        public ConnectEndpoint(string path, ObjectCacheStore store, PluginBase pbase)
        {
            InitPathAndLog(path, pbase.Log);
            Store = store;//Load client public key to verify signed messages
            Pbase = pbase;

            StatefulEventQueue = new(StringComparer.OrdinalIgnoreCase);

            //Start the queue worker
            _ = pbase.DeferTask(() => ChangeWorkerAsync(pbase.UnloadToken), 10);

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
         */

        protected override async ValueTask<VfReturnType> GetAsync(HttpEntity entity)
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
                bool verified = false;

                //Get the client public key certificate to verify the client's message
                using(ReadOnlyJsonWebKey cert = await GetClientPubAsync())
                {
                    //verify signature for client
                    if (jwt.VerifyFromJwk(cert))
                    {
                        verified = true;
                    }
                    //May be signed by a cahce server
                    else
                    {
                        using ReadOnlyJsonWebKey cacheCert = await GetCachePubAsync();
                        
                        //Set peer and verified flag since the another cache server signed the request
                        isPeer = verified = jwt.VerifyFromJwk(cacheCert);
                    }
                }
              
                //Check flag
                if (!verified)
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

            Log.Debug("Received negotiation request from node {node}", nodeId);
            //Verified, now we can create an auth message with a short expiration
            using JsonWebToken auth = new();
            //Sign the auth message from the cache certificate's private key
            using (ReadOnlyJsonWebKey cert = await GetCachePrivateKeyAsync())
            {
                auth.WriteHeader(cert.JwtHeader);
                auth.InitPayloadClaim()
                    .AddClaim("aud", AudienceLocalServerId)
                    .AddClaim("exp", entity.RequestedTimeUtc.Add(AuthTokenExpiration).ToUnixTimeSeconds())
                    .AddClaim("nonce", RandomHash.GetRandomBase32(8))
                    .AddClaim("chl", challenge!)
                    //Set the ispeer flag if the request was signed by a cache server
                    .AddClaim("isPeer", isPeer)
                    //Specify the server's node id if set
                    .AddClaim("sub", nodeId!)
                    //Add negotiaion args
                    .AddClaim(FBMClient.REQ_HEAD_BUF_QUERY_ARG, MAX_HEAD_BUF_SIZE)
                    .AddClaim(FBMClient.REQ_RECV_BUF_QUERY_ARG, MAX_RECV_BUF_SIZE)
                    .AddClaim(FBMClient.REQ_MAX_MESS_QUERY_ARG, MAX_MESSAGE_SIZE)
                    .CommitClaims();

                auth.SignFromJwk(cert);
            }
         
            //Close response
            entity.CloseResponse(HttpStatusCode.OK, ContentType.Text, auth.DataBuffer);
            return VfReturnType.VirtualSkip;
        }

        private async Task<ReadOnlyJsonWebKey> GetClientPubAsync()
        {
            return await Pbase.TryGetSecretAsync("client_public_key").ToJsonWebKey() ?? throw new KeyNotFoundException("Missing required secret : client_public_key");
        }
        private async Task<ReadOnlyJsonWebKey> GetCachePubAsync()
        {
            return await Pbase.TryGetSecretAsync("cache_public_key").ToJsonWebKey() ?? throw new KeyNotFoundException("Missing required secret : client_public_key");
        }
        private async Task<ReadOnlyJsonWebKey> GetCachePrivateKeyAsync()
        {
            return await Pbase.TryGetSecretAsync("cache_private_key").ToJsonWebKey() ?? throw new KeyNotFoundException("Missing required secret : client_public_key");
        }

        private async Task ChangeWorkerAsync(CancellationToken cancellation)
        {
            try
            {
                //Listen for changes
                while (true)
                {
                    ChangeEvent ev = await Store.EventQueue.DequeueAsync(cancellation);
                    //Add event to queues
                    foreach (AsyncQueue<ChangeEvent> queue in StatefulEventQueue.Values)
                    {
                        if (!queue.TryEnque(ev))
                        {
                            Log.Debug("Listener queue has exeeded capacity, change events will be lost");
                        }
                    }
                }
            }
            catch (OperationCanceledException)
            { }
            catch (Exception ex)
            {
                Log.Error(ex);
            }
        }

        private class WsUserState
        {
            public int RecvBufferSize { get; init; }
            public int MaxHeaderBufferSize { get; init; }
            public int MaxMessageSize { get; init; }
            public int MaxResponseBufferSize { get; init; }
            public AsyncQueue<ChangeEvent>? SyncQueue { get; init; }
        }

        protected override async ValueTask<VfReturnType> WebsocketRequestedAsync(HttpEntity entity)
        {
            try
            {
                //Parse jwt from authorization
                string? jwtAuth = entity.Server.Headers[HttpRequestHeader.Authorization];
                if (string.IsNullOrWhiteSpace(jwtAuth))
                {
                    entity.CloseResponse(HttpStatusCode.Unauthorized);
                    return VfReturnType.VirtualSkip;
                }
                
                string? nodeId = null;
                //Parse jwt
                using (JsonWebToken jwt = JsonWebToken.Parse(jwtAuth))
                {
                    //Get the client public key certificate to verify the client's message
                    using (ReadOnlyJsonWebKey cert = await GetCachePubAsync())
                    {
                        //verify signature against the cache public key, since this server must have signed it
                        if (!jwt.VerifyFromJwk(cert))
                        {
                            entity.CloseResponse(HttpStatusCode.Unauthorized);
                            return VfReturnType.VirtualSkip;
                        }
                    }
                    
                    //Recover json body
                    using JsonDocument doc = jwt.GetPayload();

                    //Verify audience, expiration

                    if (!doc.RootElement.TryGetProperty("aud", out JsonElement audEl) || !AudienceLocalServerId.Equals(audEl.GetString(), StringComparison.OrdinalIgnoreCase))
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

                    //Check if the client is a peer
                    bool isPeer = doc.RootElement.TryGetProperty("isPeer", out JsonElement isPeerEl) && isPeerEl.GetBoolean();

                    //The node id is optional and stored in the 'sub' field, ignore if the client is not a peer
                    if (isPeer && doc.RootElement.TryGetProperty("sub", out JsonElement servIdEl))
                    {
                        nodeId = servIdEl.GetString();
                    }
                }
                
                //Get query config suggestions from the client
                string recvBufCmd = entity.QueryArgs[FBMClient.REQ_RECV_BUF_QUERY_ARG];
                string maxHeaderCharCmd = entity.QueryArgs[FBMClient.REQ_HEAD_BUF_QUERY_ARG];
                string maxMessageSizeCmd = entity.QueryArgs[FBMClient.REQ_MAX_MESS_QUERY_ARG];
                
                //Parse recv buffer size
                int recvBufSize = int.TryParse(recvBufCmd, out int rbs) ? rbs : MIN_RECV_BUF_SIZE;
                int maxHeadBufSize = int.TryParse(maxHeaderCharCmd, out int hbs) ? hbs : MIN_HEAD_BUF_SIZE;
                int maxMessageSize = int.TryParse(maxMessageSizeCmd, out int mxs) ? mxs : MIN_MESSAGE_SIZE;
                
                AsyncQueue<ChangeEvent>? nodeQueue = null;
                //The connection may be a caching server node, so get its node-id
                if (!string.IsNullOrWhiteSpace(nodeId))
                {
                    /*
                     * Store a new async queue, or get an old queue for the current node
                     * 
                     * We should use a bounded queue and disacard LRU items, we also know
                     * only a single writer is needed as the queue is processed on a single thread
                     * and change events may be processed on mutliple threads.
                    */

                    BoundedChannelOptions queueOptions = new(MAX_EVENT_QUEUE_SIZE)
                    {
                        AllowSynchronousContinuations = true,
                        SingleReader = false,
                        SingleWriter = true,
                        //Drop oldest item in queue if full
                        FullMode = BoundedChannelFullMode.DropOldest,
                    };

                    _ = StatefulEventQueue.TryAdd(nodeId, new(queueOptions));
                    //Get the queue
                    nodeQueue = StatefulEventQueue[nodeId];
                }
                
                //Init new ws state object and clamp the suggested buffer sizes
                WsUserState state = new()
                {
                    RecvBufferSize = Math.Clamp(recvBufSize, MIN_RECV_BUF_SIZE, MAX_RECV_BUF_SIZE),
                    MaxHeaderBufferSize = Math.Clamp(maxHeadBufSize, MIN_HEAD_BUF_SIZE, MAX_HEAD_BUF_SIZE),
                    MaxMessageSize = Math.Clamp(maxMessageSize, MIN_MESSAGE_SIZE, MAX_MESSAGE_SIZE),
                    MaxResponseBufferSize = Math.Min(maxMessageSize, MAX_RESPONSE_BUFFER_SIZE),
                    SyncQueue = nodeQueue
                };
                
                Log.Debug("Client recv buffer suggestion {recv}, header buffer size {head}, response buffer size {r}", recvBufCmd, maxHeaderCharCmd, state.MaxResponseBufferSize);
                
                //Accept socket and pass state object
                entity.AcceptWebSocket(WebsocketAcceptedAsync, state);
                return VfReturnType.VirtualSkip;
            }
            catch (KeyNotFoundException)
            {
                return VfReturnType.BadRequest;
            }
        }
        
        private async Task WebsocketAcceptedAsync(WebSocketSession wss)
        {
            //Inc connected count
            Interlocked.Increment(ref _connectedClients);
            //Register plugin exit token to cancel the connected socket
            CancellationTokenRegistration reg = Pbase.UnloadToken.Register(wss.CancelAll);
            try
            {
                WsUserState state = (wss.UserState as WsUserState)!;

                //Init listener args from request
                FBMListenerSessionParams args = new()
                {
                    MaxMessageSize = state.MaxMessageSize,
                    RecvBufferSize = state.RecvBufferSize,
                    ResponseBufferSize = state.MaxResponseBufferSize,
                    MaxHeaderBufferSize = state.MaxHeaderBufferSize,
                    HeaderEncoding = Helpers.DefaultEncoding,
                };

                //Listen for requests
                await Store.ListenAsync(wss, args, state.SyncQueue);
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
            finally
            {
                //Dec connected count
                Interlocked.Decrement(ref _connectedClients);
                //Unregister the 
                reg.Unregister();
            }
            Log.Debug("Server websocket exited");
        }
    }
}
