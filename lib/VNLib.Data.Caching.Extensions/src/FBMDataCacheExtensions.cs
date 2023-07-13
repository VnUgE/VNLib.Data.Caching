/*
* Copyright (c) 2023 Vaughn Nugent
* 
* Library: VNLib
* Package: VNLib.Data.Caching.Extensions
* File: FBMDataCacheExtensions.cs 
*
* FBMDataCacheExtensions.cs is part of VNLib.Data.Caching.Extensions which is part of the larger 
* VNLib collection of libraries and utilities.
*
* VNLib.Data.Caching.Extensions is free software: you can redistribute it and/or modify 
* it under the terms of the GNU Affero General Public License as 
* published by the Free Software Foundation, either version 3 of the
* License, or (at your option) any later version.
*
* VNLib.Data.Caching.Extensions is distributed in the hope that it will be useful,
* but WITHOUT ANY WARRANTY; without even the implied warranty of
* MERCHANTABILITY or FITNESS FOR A PARTICULAR PURPOSE. See the
* GNU Affero General Public License for more details.
*
* You should have received a copy of the GNU Affero General Public License
* along with this program.  If not, see https://www.gnu.org/licenses/.
*/

using System;
using System.Net;
using System.Linq;
using System.Security;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using System.Collections.Generic;
using System.Security.Cryptography;
using System.Runtime.CompilerServices;

using RestSharp;

using VNLib.Hashing;
using VNLib.Hashing.IdentityUtility;
using VNLib.Utils.Memory;
using VNLib.Utils.Logging;
using VNLib.Utils.Extensions;
using VNLib.Net.Messaging.FBM;
using VNLib.Net.Messaging.FBM.Client;
using VNLib.Net.Rest.Client.Construction;
using VNLib.Data.Caching.Extensions.ApiModel;
using VNLib.Data.Caching.Extensions.Clustering;

namespace VNLib.Data.Caching.Extensions
{

    /// <summary>
    /// Provides extension methods for FBM data caching using 
    /// cache servers and brokers
    /// </summary>
    public static class FBMDataCacheExtensions
    {
        /// <summary>
        /// The websocket sub-protocol to use when connecting to cache servers
        /// </summary>
        public const string CACHE_WS_SUB_PROCOL = "object-cache";
        
        /// <summary>
        /// The default cache message header size
        /// </summary>
        public const int MAX_FBM_MESSAGE_HEADER_SIZE = 1024;

        /// <summary>
        /// The client nonce signature http header name
        /// </summary>
        public const string X_UPGRADE_SIG_HEADER = "X-Cache-Upgrade-Sig";

        /// <summary>
        /// The advertisment header for cache node discovery
        /// </summary>
        public const string X_NODE_DISCOVERY_HEADER = "X-Cache-Node-Discovery";

        /*
         * Lazy to defer errors for debuggong
         */
        private static readonly Lazy<CacheSiteAdapter> SiteAdapter = new(() => ConfigureAdapter(2));

        private static CacheSiteAdapter ConfigureAdapter(int maxClients)
        {
            CacheSiteAdapter adapter = new(maxClients);
            //Configure the site endpoints
            adapter.BuildEndpoints(ServiceEndpoints.Definition);
            return adapter;
        }

        private static readonly ConditionalWeakTable<FBMClient, CacheClientConfiguration> ClientCacheConfig = new();

        /// <summary>
        /// Gets a <see cref="FBMClientConfig"/> preconfigured object caching
        /// protocl
        /// </summary>
        /// <param name="heap">The client buffer heap</param>
        /// <param name="maxMessageSize">The maxium message size (in bytes)</param>
        /// <param name="debugLog">An optional debug log</param>
        /// <param name="timeout">Request message timeout</param>
        /// <returns>A preconfigured <see cref="FBMClientConfig"/> for object caching</returns>
        public static FBMClientConfig GetDefaultConfig(IUnmangedHeap heap, int maxMessageSize, TimeSpan timeout = default, ILogProvider? debugLog = null)
        {
            /*
             * Max message size (for server) should account for max data + the additional header buffer
             */
            int maxExtra = (int)Helpers.ToNearestKb((int)(maxMessageSize * 1.2) + MAX_FBM_MESSAGE_HEADER_SIZE);

            return new()
            {
                BufferHeap = heap,
               
                //Max message size is referrences 
                MaxMessageSize = maxExtra,

                //The size of the buffer used for buffering incoming messages
                RecvBufferSize = maxExtra,

                //Message buffer should be max message + headers
                MessageBufferSize = (int)Helpers.ToNearestKb(maxMessageSize + MAX_FBM_MESSAGE_HEADER_SIZE),

                //Caching only requires a fixed number of request headers, so we can used a fixed buffer size
                MaxHeaderBufferSize = MAX_FBM_MESSAGE_HEADER_SIZE,

                //Set the optional cache sub-protocol
                SubProtocol = CACHE_WS_SUB_PROCOL,

                HeaderEncoding = Helpers.DefaultEncoding,

                KeepAliveInterval = TimeSpan.FromSeconds(30),

                RequestTimeout = timeout,

                DebugLog = debugLog
            };
        }

        private static void LogDebug(this FBMClient client, string message)
        {
            client.Config.DebugLog?.Debug("{debug}: {data}", "[CACHE]", message);
        }
     
        /// <summary>
        /// Discovers ALL possible cache nodes itteritivley, first by collecting the configuration
        /// from the initial peers.
        /// This will make connections to all discoverable servers
        /// </summary>
        /// <param name="config"></param>
        /// <param name="cancellation">A token to cancel the operation</param>
        /// <returns></returns>
        /// <exception cref="ArgumentException"></exception>
        /// <exception cref="CacheDiscoveryFailureException"></exception>
        public static async Task DiscoverNodesAsync(this CacheClientConfiguration config, CancellationToken cancellation)
        {
            //Make sure at least one node defined
            if(config?.WellKnownNodes == null || config.WellKnownNodes.Length == 0)
            {
                throw new ArgumentException("There must be at least one cache node defined in the client configuration");
            }

            //Get the initial advertisments that arent null
            CacheNodeAdvertisment[] initialPeers = await ResolveWellKnownAsync(config, cancellation);

            if (initialPeers.Length == 0)
            {
                throw new CacheDiscoveryFailureException("There must be at least one available cache node to continue discovery");
            }

            await DiscoverNodesAsync(config, initialPeers, cancellation);
        }

        /// <summary>
        /// Resolves the initial well-known cache nodes into their advertisments
        /// </summary>
        /// <param name="config"></param>
        /// <param name="cancellation">A token to cancel the operation</param>
        /// <returns>An array of resolved nodes</returns>
        public static async Task<CacheNodeAdvertisment[]> ResolveWellKnownAsync(this CacheClientConfiguration config, CancellationToken cancellation)
        {
            _ = config?.WellKnownNodes ?? throw new ArgumentNullException(nameof(config));

            Task<CacheNodeAdvertisment?>[] initialAdds = new Task<CacheNodeAdvertisment?>[config.WellKnownNodes.Length];

            //Discover initial advertisments from well-known addresses
            for (int i = 0; i < config.WellKnownNodes.Length; i++)
            {
                initialAdds[i] = DiscoverNodeConfigAsync(config.WellKnownNodes[i], config, cancellation);
            }

            //Wait for all initial adds to complete
            await Task.WhenAll(initialAdds);

            //Get the initial advertisments that arent null
            return initialAdds.Select(static x => x.Result!).Where(static s => s != null).ToArray();
        }

        /// <summary>
        /// Discovers ALL possible cache nodes itteritivley from the current collection of initial peers.
        /// This will make connections to all discoverable servers and update the client configuration, with all 
        /// discovered peers
        /// </summary>
        /// <param name="config"></param>
        /// <param name="initialPeers">Accepts an array of initial peers to override the endpoint discovery process</param>
        /// <param name="cancellation">A token to cancel the operation</param>
        /// <returns>A task that completes when all nodes have been discovered</returns>
        /// <exception cref="ArgumentException"></exception>
        /// <exception cref="CacheDiscoveryFailureException"></exception>
        public static async Task DiscoverNodesAsync(this CacheClientConfiguration config, CacheNodeAdvertisment[] initialPeers, CancellationToken cancellation)
        {
            //Make sure at least one node defined
            if (initialPeers == null || initialPeers.Length == 0)
            {
                throw new ArgumentException("There must be at least one initial peer");
            }          

            //Get the discovery enumerator with the initial peers
            INodeDiscoveryEnumerator enumerator = config.NodeCollection.BeginDiscovery(initialPeers);

            //Start the discovery process
            await DiscoverNodesAsync(enumerator, config, config.ErrorHandler, cancellation);

            //Commit nodes
            config.NodeCollection.CompleteDiscovery(enumerator);
        }

        private static async Task DiscoverNodesAsync(
            INodeDiscoveryEnumerator enumerator,
            CacheClientConfiguration config,
            ICacheDiscoveryErrorHandler? errHandler,
            CancellationToken cancellation
        )
        {
            //Loop through servers
            while (enumerator.MoveNext())
            {
                //Make sure the node has a discovery endpoint
                if (enumerator.Current.DiscoveryEndpoint == null)
                {
                    //Skip this node
                    continue;
                }

                /*
                 * We are allowed to save nodes that do not have a discovery endpoint, but we cannot 
                 * discover nodes from them we can only use them as cache
                 */

                //add a random delay to avoid spamming servers
                await Task.Delay((int)Random.Shared.NextInt64(100, 500), cancellation);

                try
                {
                    //Discover nodes from the current node
                    CacheNodeAdvertisment[]? nodes = await GetCacheNodesAsync(enumerator.Current, config, cancellation);

                    if (nodes != null)
                    {
                        //Add nodes to the collection
                        enumerator.OnPeerDiscoveryComplete(nodes);
                    }
                }
                //Catch exceptions when an error handler is defined
                catch(Exception ex) when (errHandler != null)
                {
                    //Handle the error
                    errHandler.OnDiscoveryError(enumerator.Current, ex);
                }
                catch(Exception ex)
                {
                    throw new CacheDiscoveryFailureException($"Failed to discovery peer node {enumerator.Current?.NodeId}, cannot continue", ex);
                }
            }
        }

        private static async Task<CacheNodeAdvertisment?> DiscoverNodeConfigAsync(Uri serverUri, CacheClientConfiguration config, CancellationToken cancellation)
        {
            try
            {
                GetConfigRequest req = new (serverUri, config);

                //Site adapter verifies response messages so we dont need to check on the response
                byte[] data = await SiteAdapter.Value.ExecuteAsync(req, cancellation).AsBytes() 
                        ?? throw new CacheDiscoveryFailureException($"No data returned from desired cache node");

                //Response is jwt
                using JsonWebToken responseJwt = JsonWebToken.ParseRaw(data);

                //The entire payload is just the single serialzed advertisment
                using JsonDocument doc =  responseJwt.GetPayload();

                return doc.RootElement.GetProperty("sub").Deserialize<CacheNodeAdvertisment>();
            }
            //Bypass cdfe when error handler is null
            catch(CacheDiscoveryFailureException) when(config.ErrorHandler == null)
            {
                throw;
            }
            //Catch exceptions when an error handler is defined
            catch (Exception ex) when (config.ErrorHandler != null)
            {
                //Handle the error
                config.ErrorHandler.OnDiscoveryError(null!, ex);
                return null;
            }
            catch (Exception ex)
            {
                throw new CacheDiscoveryFailureException("Failed to discover node configuration", ex);
            }
        }

        /// <summary>
        /// Contacts the given server's discovery endpoint to discover a list of available 
        /// servers we can connect to
        /// </summary>
        /// <param name="advert">An advertisment of a server to discover other nodes from</param>
        /// <param name="cancellationToken">A token to cancel the operationS</param>
        /// <param name="config">The cache configuration object</param>
        /// <returns>The list of active servers</returns>
        /// <exception cref="SecurityException"></exception>
        /// <exception cref="ArgumentException"></exception>
        /// <exception cref="ArgumentNullException"></exception>
        public static async Task<CacheNodeAdvertisment[]?> GetCacheNodesAsync(CacheNodeAdvertisment advert, CacheClientConfiguration config, CancellationToken cancellationToken = default)
        {
            _ = advert ?? throw new ArgumentNullException(nameof(advert));
            _ = config ?? throw new ArgumentNullException(nameof(config));
            _ = advert.DiscoveryEndpoint ?? throw new ArgumentException("Advertisment does not expose an advertisment endpoint");

            DiscoveryRequest req = new (advert.DiscoveryEndpoint, config);

            //Site adapter verifies response messages so we dont need to check on the response
            byte[] data = await SiteAdapter.Value.ExecuteAsync(req, cancellationToken).AsBytes()
                ?? throw new InvalidOperationException($"No data returned from node {advert.NodeId}");

            //Response is jwt
            using JsonWebToken responseJwt = JsonWebToken.ParseRaw(data);

            using JsonDocument doc = responseJwt.GetPayload();
            return doc.RootElement.GetProperty("peers").Deserialize<CacheNodeAdvertisment[]>();
        }

        /// <summary>
        /// Allows for configuration of an <see cref="FBMClient"/>
        /// for a connection to a cache server
        /// </summary>
        /// <param name="client"></param>
        /// <returns>A fluent api configuration builder for the current client</returns>
        public static CacheClientConfiguration GetCacheConfiguration(this FBMClient client) => ClientCacheConfig.GetOrCreateValue(client);

        /// <summary>
        /// Explicitly set the client cache configuration for the current client
        /// </summary>
        /// <param name="client"></param>
        /// <param name="config">The cache node configuration</param>
        /// <returns>The config instance</returns>
        public static CacheClientConfiguration SetCacheConfiguration(this FBMClient client, CacheClientConfiguration config)
        {
            ClientCacheConfig.AddOrUpdate(client, config);
            return config;
        }

        /// <summary>
        /// Explicitly set the cache node configuration for the current client
        /// </summary>
        /// <param name="client"></param>
        /// <param name="nodeConfig">The cache node configuration</param>
        /// <returns>The config instance</returns>
        public static CacheNodeConfiguration SetCacheConfiguration(this FBMClient client, CacheNodeConfiguration nodeConfig)
        {
            ClientCacheConfig.AddOrUpdate(client, nodeConfig);
            return nodeConfig;
        }

        /// <summary>
        /// Waits for the client to disconnect from the server while observing 
        /// the cancellation token. If the token is cancelled, the connection is 
        /// closed cleanly if possible
        /// </summary>
        /// <param name="client"></param>
        /// <param name="token">A token to cancel the connection to the server</param>
        /// <exception cref="TaskCanceledException"></exception>
        /// <exception cref="ObjectDisposedException"></exception>
        /// <returns>A task that complets when the connecion has been closed successfully</returns>
        public static async Task WaitForExitAsync(this FBMClient client, CancellationToken token = default)
        {
            client.LogDebug("Waiting for cache client to exit");

            //Get task for cancellation
            Task cancellation = token.WaitHandle.WaitAsync();

            //Task for status handle
            Task run = client.ConnectionStatusHandle.WaitAsync();

            //Wait for cancellation or 
            _ = await Task.WhenAny(cancellation, run);
            
            client.LogDebug("Disconnecting the cache client");

            //Normal try to disconnect the socket
            await client.DisconnectAsync(CancellationToken.None);

            //If the cancellation is completed, throw a task cancelled exception
            if (cancellation.IsCompleted)
            {
                throw new TaskCanceledException("The client disconnected because the connection was cancelled");
            }
        }

        /// <summary>
        /// Discovers all available nodes for the current client config
        /// </summary>
        /// <param name="client"></param>
        /// <param name="cancellation">A token to cancel the operation</param>
        /// <returns>A task that completes when all nodes have been discovered</returns>
        public static Task DiscoverAvailableNodesAsync(this FBMClient client, CancellationToken cancellation = default)
        {
            //Get stored config
            CacheClientConfiguration conf = ClientCacheConfig.GetOrCreateValue(client);

            //Discover all nodes
            return conf.DiscoverNodesAsync(cancellation);
        }

        /// <summary>
        /// Connects to a random server from the servers discovered during a cache server discovery
        /// </summary>
        /// <param name="client"></param>
        /// <param name="cancellation">A token to cancel the operation</param>
        /// <returns>The server that the connection was made with</returns>
        /// <exception cref="FBMException"></exception>
        /// <exception cref="FBMServerNegiationException"></exception>
        /// <exception cref="ArgumentException"></exception>
        /// <exception cref="ArgumentNullException"></exception>
        /// <exception cref="SecurityException"></exception>
        /// <exception cref="ObjectDisposedException"></exception>
        public static async Task<CacheNodeAdvertisment> ConnectToRandomCacheAsync(this FBMClient client, CancellationToken cancellation = default)
        {
            //Get stored config
            CacheClientConfiguration conf = ClientCacheConfig.GetOrCreateValue(client);

            //Get all available nodes, or at least the initial peers
            CacheNodeAdvertisment[]? adverts = conf.NodeCollection.GetAllNodes() ?? throw new ArgumentException("No cache nodes discovered, cannot connect");

            //Select random node from all available nodes
            CacheNodeAdvertisment randomServer = adverts.SelectRandom();

            //Connect to the random server
            await ConnectToCacheAsync(client, randomServer, cancellation);

            //Return the random server we connected to
            return randomServer;
        }

        /// <summary>
        /// Connects to the specified server on the configured cache client
        /// </summary>
        /// <param name="client"></param>
        /// <param name="server">The server to connect to</param>
        /// <param name="token">A token to cancel the operation</param>
        /// <returns>A task that resolves when the client is connected to the cache server</returns>
        /// <exception cref="FBMException"></exception>
        /// <exception cref="FBMServerNegiationException"></exception>
        /// <exception cref="ArgumentException"></exception>
        /// <exception cref="ArgumentNullException"></exception>
        /// <exception cref="SecurityException"></exception>
        /// <exception cref="ObjectDisposedException"></exception>
        public static Task ConnectToCacheAsync(this FBMClient client, CacheNodeAdvertisment server, CancellationToken token = default)
        {
            _ = client ?? throw new ArgumentNullException(nameof(client));
            _ = server ?? throw new ArgumentNullException(nameof(server));
            
            //Get stored config
            CacheClientConfiguration conf = ClientCacheConfig.GetOrCreateValue(client);

            //Connect to server (no server id because client not replication server)
            return ConnectToCacheAsync(client, conf, server, token);
        }

        /// <summary>
        /// Connects to the specified server on the configured cache client
        /// </summary>
        /// <param name="client"></param>
        /// <param name="server">The server to connect to</param>
        /// <param name="token">A token to cancel the operation</param>
        /// <param name="explicitConfig">Explicit cache configuration to use</param>
        /// <returns>A task that resolves when the client is connected to the cache server</returns>
        /// <exception cref="FBMException"></exception>
        /// <exception cref="FBMServerNegiationException"></exception>
        /// <exception cref="ArgumentException"></exception>
        /// <exception cref="ArgumentNullException"></exception>
        /// <exception cref="SecurityException"></exception>
        /// <exception cref="ObjectDisposedException"></exception>
        public static Task ConnectToCacheAsync(this FBMClient client, CacheNodeAdvertisment server, CacheClientConfiguration explicitConfig, CancellationToken token = default)
        {
            _ = client ?? throw new ArgumentNullException(nameof(client));
            _ = server ?? throw new ArgumentNullException(nameof(server));
           
            //Connect to server (no server id because client not replication server)
            return ConnectToCacheAsync(client, explicitConfig, server, token);
        }


        private static async Task ConnectToCacheAsync(
            FBMClient client, 
            CacheClientConfiguration config, 
            CacheNodeAdvertisment server, 
            CancellationToken token = default
        )
        {
            /*
             * During a server negiation, the client makes an intial get request to the cache endpoint
             * and passes some client negiation terms as a signed message to the server. The server then
             * validates these values and returns a signed jwt with the server negiation terms. 
             * 
             * The response from the server is essentailly the 'access token'  
             */

            client.LogDebug("Negotiating with cache server");

            //Create a new connection negotiation
            NegotationRequest req = new(server.ConnectEndpoint, config);

            //Exec negotiation
            RestResponse response = await SiteAdapter.Value.ExecuteAsync(req, token);

            /*
             * JWT will already be veified by the endpoint adapter, so we 
             * just need to validate the server's buffer configuration
             */
            using (JsonWebToken jwt = JsonWebToken.ParseRaw(response.RawBytes))
            {                
                //Confirm the server's buffer configuration
                 ValidateServerNegotation(client, jwt);
            }
            
            client.LogDebug("Server negotiation validated, connecting to server");

            //The client authorization header is the exact response
            client.ClientSocket.Headers[HttpRequestHeader.Authorization] = response.Content!;

            //See if the supplied config is for a cache node
            CacheNodeConfiguration? cnc = config as CacheNodeConfiguration;

            //Compute the signature of the upgrade token
            client.ClientSocket.Headers[X_UPGRADE_SIG_HEADER] = config.AuthManager.GetBase64UpgradeSignature(response.Content, cnc != null);

            //Check to see if adversize self is enabled
            if (cnc?.BroadcastAdverisment == true)
            {
                //Set advertisment header
                client.ClientSocket.Headers[X_NODE_DISCOVERY_HEADER] = GetAdvertismentHeader(cnc);
            }

            //build ws uri from the connect endpoint
            UriBuilder uriBuilder = new(server.ConnectEndpoint)
            {
                Scheme = config.UseTls ? "wss://" : "ws://"
            };

            //Connect async
            await client.ConnectAsync(uriBuilder.Uri, token);
        }

        private static void ValidateServerNegotation(FBMClient client, JsonWebToken jwt)
        {
            try
            {
                //Get the response message to verify the challenge, and client arguments
                using JsonDocument doc = jwt.GetPayload();

                IReadOnlyDictionary<string, JsonElement> args = doc.RootElement
                                                            .EnumerateObject()
                                                            .ToDictionary(static k => k.Name, static v => v.Value);

                //Get the negiation values
                uint recvBufSize = args[FBMClient.REQ_RECV_BUF_QUERY_ARG].GetUInt32();
                uint headerBufSize = args[FBMClient.REQ_HEAD_BUF_QUERY_ARG].GetUInt32();
                uint maxMessSize = args[FBMClient.REQ_MAX_MESS_QUERY_ARG].GetUInt32();
                
                //Verify the values
                if (client.Config.RecvBufferSize > recvBufSize)
                {
                    throw new FBMServerNegiationException("Failed to negotiate with the server, the server's recv buffer size is too small");
                }

                if (client.Config.MaxHeaderBufferSize > headerBufSize)
                {
                    throw new FBMServerNegiationException("Failed to negotiate with the server, the server's header buffer size is too small");
                }

                if (client.Config.MaxMessageSize > maxMessSize)
                {
                    throw new FBMServerNegiationException("Failed to negotiate with the server, the server's max message size is too small");
                }
            }
            catch (FBMException)
            {
                throw;
            }
            catch (Exception ex)
            {
                throw new FBMServerNegiationException("Negotiation with the server failed", ex);
            }
        }

        /*
         * Added layer to confirm that client that requested the negotation holds the private key
         * compute a signature of the upgrade token and send it to the server to prove we hold the private key.
         */

        private static string GetBase64UpgradeSignature(this ICacheAuthManager man, string? token, bool isPeer)
        {
            //Compute hash of the token
            byte[] hash = ManagedHash.ComputeHash(token, HashAlg.SHA256);

            //Sign the hash
            byte[] sig = man.SignMessageHash(hash, HashAlg.SHA256);

            //Return the base64 string
            return Convert.ToBase64String(sig);
        }
       

        /// <summary>
        /// Verifies the signed auth token against the given verification key
        /// </summary>
        /// <param name="man"></param>
        /// <param name="signature">The base64 signature of the token</param>
        /// <param name="token">The raw token to compute the hash of</param>
        /// <param name="isPeer">A value that indicates if the connection is from a peer node</param>
        /// <returns>True if the singature matches, false otherwise</returns>
        /// <exception cref="ArgumentNullException"></exception>
        /// <exception cref="CryptographicException"></exception>
        public static bool VerifyUpgradeToken(this ICacheAuthManager man, string signature, string token, bool isPeer)
        {
            _ = man ?? throw new ArgumentNullException(nameof(man));

            //get the hash of the token
            byte[] hash = ManagedHash.ComputeHash(token, HashAlg.SHA256);

            //decode the signature
            byte[] sig = Convert.FromBase64String(signature);

            return man.VerifyMessageHash(hash, HashAlg.SHA256, sig, isPeer);
        }

        private static string GetAdvertismentHeader(CacheNodeConfiguration nodeConfiguration)
        {
            /*
             * Create node advertisment message to publish to peer nodes
             * 
             * these messages will allow other clients and peers to discover us
             */

            using JsonWebToken jwt = new();

            //Get the jwt header
            jwt.WriteHeader(nodeConfiguration.AuthManager.GetJwtHeader());

            jwt.InitPayloadClaim()
                .AddClaim("nonce", RandomHash.GetRandomBase32(16))
                .AddClaim("iat", DateTimeOffset.UtcNow.ToUnixTimeSeconds())
                .AddClaim("iss", nodeConfiguration.NodeId!)
                .AddClaim("url", nodeConfiguration.ConnectEndpoint!.ToString())
                //Optional discovery endpoint
                .AddClaim("dis", nodeConfiguration.DiscoveryEndpoint?.ToString() ?? string.Empty)
                .CommitClaims();

            //Sign message
            nodeConfiguration.AuthManager.SignJwt(jwt);

            return jwt.Compile();
        }

        /// <summary>
        /// Verifies the peer advertisment message
        /// </summary>
        /// <param name="config"></param>
        /// <param name="message">The advertisment message to verify</param>
        /// <returns>The advertisment message if successfully verified, or null otherwise</returns>
        /// <exception cref="FormatException"></exception>
        public static CacheNodeAdvertisment? VerifyPeerAdvertisment(this ICacheAuthManager config, string message)
        {
            using JsonWebToken jwt = JsonWebToken.Parse(message);

            //Verify the signature
            if (!config.VerifyJwt(jwt, true))
            {
                return null;
            }

            //Get the payload
            return jwt.GetPayload<CacheNodeAdvertisment>();
        }        

        /// <summary>
        /// Selects a random server from a collection of active servers
        /// </summary>
        /// <param name="servers"></param>
        /// <returns>A server selected at random</returns>
        public static CacheNodeAdvertisment SelectRandom(this ICollection<CacheNodeAdvertisment> servers)
        {
            //select random server
            int randServer = RandomNumberGenerator.GetInt32(0, servers.Count);
            return servers.ElementAt(randServer);
        }
    }
}
