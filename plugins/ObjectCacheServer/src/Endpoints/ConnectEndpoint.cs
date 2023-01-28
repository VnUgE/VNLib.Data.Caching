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

using VNLib.Plugins;
using VNLib.Hashing;
using VNLib.Net.Http;
using VNLib.Utils.Async;
using VNLib.Utils.Memory;
using VNLib.Utils.Logging;
using VNLib.Hashing.IdentityUtility;
using VNLib.Net.Messaging.FBM;
using VNLib.Net.Messaging.FBM.Client;
using VNLib.Net.Messaging.FBM.Server;
using VNLib.Plugins.Essentials;
using VNLib.Plugins.Extensions.Loading;
using VNLib.Plugins.Essentials.Endpoints;
using VNLib.Plugins.Essentials.Extensions;
using System.Text.Json.Serialization;

namespace VNLib.Data.Caching.ObjectCache.Server
{

    [ConfigurationName("store")]
    internal sealed class ConnectEndpoint : ResourceEndpointBase, IDisposable, IAsyncBackgroundWork
    {
        private static readonly TimeSpan AuthTokenExpiration = TimeSpan.FromSeconds(30);      

        private readonly string AudienceLocalServerId;
        private readonly ObjectCacheStore Store;
        private readonly PluginBase Pbase;

        private readonly ConcurrentDictionary<string, AsyncQueue<ChangeEvent>> StatefulEventQueue;

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
        protected override ProtectionSettings EndpointProtectionSettings { get; } = new()
        {
            DisableBrowsersOnly = true,
            DisableSessionsRequired = true,
            DisableCrossSiteDenied = true
        };

        public ConnectEndpoint(PluginBase plugin, IReadOnlyDictionary<string, JsonElement> config)
        {
            string? path = config["path"].GetString();

            InitPathAndLog(path, plugin.Log);
          
            Pbase = plugin;

            //Parse cache config or use default
            if(config.TryGetValue("cache", out JsonElement confEl))
            {
                CacheConfig = confEl.Deserialize<CacheConfiguration>()!;
            }
            else
            {
                //Init default config if not fount
                CacheConfig = new();

                Log.Verbose("Loading default cache buffer configuration");
            }

            //Create event queue client lookup table 
            StatefulEventQueue = new(StringComparer.OrdinalIgnoreCase);

            //Init the cache store
            Store = InitializeCache((ObjectCacheServerEntry)plugin, CacheConfig.MaxCacheEntries);

            /*
            * Generate a random guid for the current server when created so we 
            * know client tokens belong to us when singed by the same key
            */
            AudienceLocalServerId = Guid.NewGuid().ToString("N");

            //Schedule the queue worker to be run
            _ = plugin.ObserveWork(this, 100);
        }

        private static ObjectCacheStore InitializeCache(ObjectCacheServerEntry plugin, int maxCache)
        {
            if(maxCache < 2)
            {
                throw new ArgumentException("You must configure a 'max_cache' size larger than 1 item");
            }

            //Suggestion
            if(maxCache < 200)
            {
                plugin.Log.Information("Suggestion: You may want a larger cache size, you have less than 200 items in cache");
            }

            //Endpoint only allows for a single reader
            return new (maxCache, plugin.Log, plugin.CacheHeap, true);
        }

        /// <summary>
        /// Gets the configured cache store
        /// </summary>
        /// <returns></returns>
        public ICacheStore GetCacheStore() => new CacheStore(Store);
      

        //Dispose will be called by the host plugin on unload
        void IDisposable.Dispose()
        {
            //Dispose the store on cleanup
            Store.Dispose();
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
                    //May be signed by a cache server
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
                    .AddClaim(FBMClient.REQ_HEAD_BUF_QUERY_ARG, CacheConfig.MaxHeaderBufferSize)
                    .AddClaim(FBMClient.REQ_RECV_BUF_QUERY_ARG, CacheConfig.MaxRecvBufferSize)
                    .AddClaim(FBMClient.REQ_MAX_MESS_QUERY_ARG, CacheConfig.MaxMessageSize)
                    .CommitClaims();

                auth.SignFromJwk(cert);
            }
         
            //Close response
            entity.CloseResponse(HttpStatusCode.OK, ContentType.Text, auth.DataBuffer);
            return VfReturnType.VirtualSkip;
        }


        //Background worker to process event queue items
        async Task IAsyncBackgroundWork.DoWorkAsync(ILogProvider pluginLog, CancellationToken exitToken)
        {
            try
            {
                //Listen for changes
                while (true)
                {
                    ChangeEvent ev = await Store.EventQueue.DequeueAsync(exitToken);

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
            {
                //Normal exit
            }
        }

        private class WsUserState
        {
            public int RecvBufferSize { get; init; }
            public int MaxHeaderBufferSize { get; init; }
            public int MaxMessageSize { get; init; }
            public int MaxResponseBufferSize { get; init; }
            public AsyncQueue<ChangeEvent>? SyncQueue { get; init; }

            public override string ToString()
            {
                return 
              $"{nameof(RecvBufferSize)}:{RecvBufferSize}, {nameof(MaxHeaderBufferSize)}: {MaxHeaderBufferSize}, {nameof(MaxMessageSize)}:{MaxMessageSize}, {nameof(MaxResponseBufferSize)}:{MaxResponseBufferSize}";
            }
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
                int recvBufSize = int.TryParse(recvBufCmd, out int rbs) ? rbs : CacheConfig.MinRecvBufferSize;
                int maxHeadBufSize = int.TryParse(maxHeaderCharCmd, out int hbs) ? hbs : CacheConfig.MinHeaderBufferSize;
                int maxMessageSize = int.TryParse(maxMessageSizeCmd, out int mxs) ? mxs : CacheConfig.MaxMessageSize;
                
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

                    BoundedChannelOptions queueOptions = new(CacheConfig.MaxEventQueueDepth)
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

                /*
                 * Buffer sizing can get messy as the response/resquest sizes can vary
                 * and will include headers, this is a drawback of the FBM protocol 
                 * so we need to properly calculate efficient buffer sizes as 
                 * negotiated with the client.
                 */

                int maxMessageSizeClamp = Math.Clamp(maxMessageSize, CacheConfig.MinRecvBufferSize, CacheConfig.MaxRecvBufferSize);

                //Init new ws state object and clamp the suggested buffer sizes
                WsUserState state = new()
                {
                    RecvBufferSize = Math.Clamp(recvBufSize, CacheConfig.MinRecvBufferSize, CacheConfig.MaxRecvBufferSize),
                    MaxHeaderBufferSize = Math.Clamp(maxHeadBufSize, CacheConfig.MinHeaderBufferSize, CacheConfig.MaxHeaderBufferSize),

                    MaxMessageSize = maxMessageSizeClamp,

                    /*
                     * Response buffer needs to be large enough to store a max message 
                     * as a response along with all response headers
                     */
                    MaxResponseBufferSize = (int)MemoryUtil.NearestPage(maxMessageSizeClamp),

                    SyncQueue = nodeQueue
                };
                
                Log.Debug("Client recv buffer suggestion {recv}, header buffer size {head}, response buffer size {r}", recvBufCmd, maxHeaderCharCmd, state.MaxResponseBufferSize);

                //Print state message to console
                Log.Verbose("Client buffer state {state}", state);
                
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
       

        private sealed class CacheStore : ICacheStore
        {
            private readonly ObjectCacheStore _cache;

            public CacheStore(ObjectCacheStore cache)
            {
                _cache = cache;
            }

            ValueTask ICacheStore.AddOrUpdateBlobAsync<T>(string objectId, string? alternateId, GetBodyDataCallback<T> bodyData, T state, CancellationToken token)
            {
                return _cache.AddOrUpdateBlobAsync(objectId, alternateId, bodyData, state, token);
            }

            void ICacheStore.Clear()
            {
                throw new NotImplementedException();
            }

            ValueTask<bool> ICacheStore.DeleteItemAsync(string id, CancellationToken token)
            {
                return _cache.DeleteItemAsync(id, token);
            }
        }
    }
}
