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
using System.Text;
using System.Linq;
using System.Security;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using System.Collections.Generic;
using System.Security.Cryptography;
using System.Text.Json.Serialization;
using System.Runtime.CompilerServices;

using RestSharp;

using VNLib.Net.Http;
using VNLib.Hashing;
using VNLib.Hashing.IdentityUtility;
using VNLib.Utils.Memory;
using VNLib.Utils.Logging;
using VNLib.Utils.Extensions;
using VNLib.Net.Rest.Client;
using VNLib.Net.Messaging.FBM;
using VNLib.Net.Messaging.FBM.Client;
using ContentType = VNLib.Net.Http.ContentType;


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

        private static readonly RestClientPool ClientPool = new(2,new RestClientOptions()
        {
            MaxTimeout = 10 * 1000,
            FollowRedirects = false,
            Encoding = Encoding.UTF8,
            AutomaticDecompression = DecompressionMethods.All,
            ThrowOnAnyError = true,
        });

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
        /// Creats a new <see cref="CacheListServerRequest"/> from an existing <see cref="CacheClientConfiguration"/>
        /// </summary>
        /// <param name="conf">The prepared client configuration</param>
        /// <returns>The new <see cref="CacheListServerRequest"/></returns>
        public static CacheListServerRequest GetListMessage(this CacheClientConfiguration conf)
        {
            return new(conf, conf.DiscoveryEndpoint);
        }

        /// <summary>
        /// Discovers peer nodes from a given initial peer and returns a list of discovered nodes. If the config 
        /// is for a cache peer node, the current peer is removed from the list of discovered nodes.
        /// </summary>
        /// <param name="cacheConfig"></param>
        /// <param name="initialPeer">The initial peer to discover nodes from</param>
        /// <param name="cancellation">A token to cancel the discovery operation</param>
        /// <returns>The collection of discovered nodes</returns>
        /// <exception cref="ArgumentNullException"></exception>
        public static async Task<ICachePeerAdvertisment[]?> DiscoverClusterNodesAsync(
             this CacheClientConfiguration cacheConfig,
             ICachePeerAdvertisment initialPeer,
             CancellationToken cancellation
        )
        {
            _ = initialPeer?.DiscoveryEndpoint ?? throw new ArgumentException("Advertisment does not expose an advertisment endpoint");

            //Create list request
            CacheListServerRequest request = cacheConfig.GetListMessage();

            //Override with the initial peer's discovery endpoint
            request.WithDiscoveryEndpoint(initialPeer.DiscoveryEndpoint);

            //Get the list of servers
            ICachePeerAdvertisment[]? servers = await ListServersAsync(request, cancellation);

            if (servers == null)
            {
                return null;
            }

            if(cacheConfig is CacheNodeConfiguration cnc)
            {
                //Filter out the current node
                return servers.Where(s => !cnc.NodeId.Equals(s.NodeId, StringComparison.OrdinalIgnoreCase)).ToArray();
            }
            else
            {
                //Do not filter 
                return servers;
            }          
        }

        /// <summary>
        /// Contacts the cache broker to get a list of active servers to connect to
        /// </summary>
        /// <param name="request">The request message used to connecto the broker server</param>
        /// <param name="cancellationToken">A token to cancel the operationS</param>
        /// <returns>The list of active servers</returns>
        /// <exception cref="SecurityException"></exception>
        /// <exception cref="ArgumentNullException"></exception>
        public static async Task<ICachePeerAdvertisment[]?> ListServersAsync(ICacheListServerRequest request, CancellationToken cancellationToken = default)
        {
            _ = request ?? throw new ArgumentNullException(nameof(request));

            string jwtBody;

            //Build request jwt
            using (JsonWebToken requestJwt = new())
            {
                requestJwt.WriteHeader(request.GetJwtHeader());
                requestJwt.InitPayloadClaim()
                    .AddClaim("iat", DateTimeOffset.UtcNow.ToUnixTimeMilliseconds())
                    .AddClaim("nonce", RandomHash.GetRandomBase32(16))
                    .CommitClaims();

                //sign the jwt
                request.SignJwt(requestJwt);

                //Compile the jwt
                jwtBody = requestJwt.Compile();
            }

            //New list request
            RestRequest listRequest = new(request.DiscoveryEndpoint, Method.Post);

            //Add the jwt as a string to the request body
            listRequest.AddStringBody(jwtBody, DataFormat.None);
            listRequest.AddHeader("Accept", HttpHelpers.GetContentTypeString(ContentType.Text));
            listRequest.AddHeader("Content-Type", HttpHelpers.GetContentTypeString(ContentType.Text));

            byte[] data;

            //Rent client
            using (ClientContract client = ClientPool.Lease())
            {
                //Exec list request
                RestResponse response = await client.Resource.ExecuteAsync(listRequest, cancellationToken);

                if (!response.IsSuccessful)
                {
                    throw response.ErrorException!;
                }

                data = response.RawBytes ?? throw new InvalidOperationException("No data returned from broker");
            }

            //Response is jwt
            using JsonWebToken responseJwt = JsonWebToken.ParseRaw(data);
            
            //Verify the jwt
            if (!request.VerifyBroker(responseJwt))
            {
                throw new SecurityException("Failed to verify the broker's challenge, cannot continue");
            }
            
            using JsonDocument doc = responseJwt.GetPayload();
            return doc.RootElement.GetProperty("peers").Deserialize<Advertisment[]>();
        }

        /// <summary>
        /// Registers the current node with the broker
        /// </summary>
        /// <returns>A task that completes when the regitration has been made successfully</returns>
        /// <exception cref="ArgumentException"></exception>
        public static async Task RegisterWithBrokerAsync(this CacheNodeConfiguration config, string authToken)
        {
            //Recover the certificate
            ReadOnlyJsonWebKey cacheCert = config?.SigningKey ?? throw new ArgumentException(nameof(config.SigningKey));

            //init broker request
            using BrokerRegistrationRequest request = new();

            request.WithBroker(config.DiscoveryEndpoint!)
                .WithRegistrationAddress(config.ConnectEndpoint!.ToString())
                .WithNodeId(config.NodeId!)
                .WithSigningKey(cacheCert, true)
                .WithHeartbeatToken(authToken);


            //Send the request
            await RegisterWithBrokerAsync(request);
        }

        /// <summary>
        /// Registers the current server as active with the specified broker
        /// </summary>
        /// <param name="registration">The registration request</param>
        public static async Task RegisterWithBrokerAsync(BrokerRegistrationRequest registration)
        {
            _ = registration ?? throw new ArgumentNullException(nameof(registration));
            _ = registration.HeartbeatToken ?? throw new ArgumentException("Missing required heartbeat access token");
            _ = registration.NodeId ?? throw new ArgumentException("Missing required cache server NodeId");
            _ = registration.BrokerAddress ?? throw new ArgumentException("Broker server address has not been configured");
            _ = registration.RegistrationAddress ?? throw new ArgumentException("Missing required registration address", nameof(registration));

            string requestData;
            //Create the jwt for signed registration message
            using (JsonWebToken jwt = new())
            {
                //Shared jwt header
                jwt.WriteHeader(registration.JsonHeader);
                //build jwt claim
                jwt.InitPayloadClaim()
                    .AddClaim("address", registration.RegistrationAddress)
                    .AddClaim("sub", registration.NodeId)
                    .AddClaim("token", registration.HeartbeatToken)
                    .CommitClaims();

                //Sign the jwt
                registration.SignJwt(jwt);
                //Compile and save
                requestData = jwt.Compile();
            }

            //Create reg request message
            RestRequest regRequest = new(registration.BrokerAddress);
            regRequest.AddStringBody(requestData, DataFormat.None);
            regRequest.AddHeader("Content-Type", "text/plain");

            //Rent client
            using ClientContract cc = ClientPool.Lease();

            //Exec the regitration request
            RestResponse response = await cc.Resource.ExecutePutAsync(regRequest);
            response.ThrowIfError();
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
        /// Discovers cache nodes in the broker configured for the current client.
        /// </summary>
        /// <param name="client"></param>
        /// <param name="token">A token to cancel the discovery</param>
        /// <returns>A task the resolves the list of active servers on the broker server</returns>
        public static Task<ICachePeerAdvertisment[]?> DiscoverCacheNodesAsync(this FBMClientWorkerBase client, CancellationToken token = default)
        {
            return client.Client.DiscoverCacheNodesAsync(token);
        }
       
        /// <summary>
        /// Discovers cache nodes in the broker configured for the current client.
        /// </summary>
        /// <param name="client"></param>
        /// <param name="token">A token to cancel the discovery </param>
        /// <returns>A task the resolves the list of active servers on the broker server</returns>
        public static async Task<ICachePeerAdvertisment[]?> DiscoverCacheNodesAsync(this FBMClient client, CancellationToken token = default)
        {
            //Get the stored client config
            CacheClientConfiguration conf = ClientCacheConfig.GetOrCreateValue(client);

            //List servers async
            return conf.CacheServers = await ListServersAsync(conf, token);
        }

        /// <summary>
        /// Waits for the client to disconnect from the server while observing 
        /// the cancellation token. If the token is cancelled, the connection is 
        /// closed cleanly if possible
        /// </summary>
        /// <param name="client"></param>
        /// <param name="token">A token to cancel the connection to the server</param>
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

            //Notify if cancelled
            token.ThrowIfCancellationRequested();
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
        public static async Task<ICachePeerAdvertisment> ConnectToRandomCacheAsync(this FBMClient client, CancellationToken cancellation = default)
        {
            //Get stored config
            CacheClientConfiguration conf = ClientCacheConfig.GetOrCreateValue(client);

            //Select random
            ICachePeerAdvertisment? randomServer = conf.CacheServers?.SelectRandom() 
                ?? throw new ArgumentException("No servers detected, cannot connect");

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
        public static Task ConnectToCacheAsync(this FBMClient client, ICachePeerAdvertisment server, CancellationToken token = default)
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
        public static Task ConnectToCacheAsync(this FBMClient client, ICachePeerAdvertisment server, CacheClientConfiguration explicitConfig, CancellationToken token = default)
        {
            _ = client ?? throw new ArgumentNullException(nameof(client));
            _ = server ?? throw new ArgumentNullException(nameof(server));
           
            //Connect to server (no server id because client not replication server)
            return ConnectToCacheAsync(client, explicitConfig, server, token);
        }


        private static async Task ConnectToCacheAsync(
            FBMClient client, 
            CacheClientConfiguration config, 
            ICachePeerAdvertisment server, 
            CancellationToken token = default
        )
        {
            //build ws uri from the connect endpoint
            UriBuilder uriBuilder = new(server.ConnectEndpoint)
            {
                Scheme = config.UseTls ? "wss://" : "ws://"
            };

            string challenge = RandomHash.GetRandomBase32(24);

            //See if the supplied config is for a cache node
            CacheNodeConfiguration? cnc = config as CacheNodeConfiguration;

            string jwtMessage;
            //Init jwt for connecting to server
            using (JsonWebToken jwt = new())
            {
                jwt.WriteHeader(config.GetJwtHeader());
                
                //Init claim
                JwtPayload claim = jwt.InitPayloadClaim();
                
                claim.AddClaim("chl", challenge);
                
                if (!string.IsNullOrWhiteSpace(cnc?.NodeId))
                {
                    /*
                    * The unique node id so the other nodes know to load the 
                    * proper event queue for the current server
                    */
                    claim.AddClaim("sub", cnc.NodeId);
                }
                
                claim.CommitClaims();

                //Sign jwt
                config.SignJwt(jwt);

                //Compile to string
                jwtMessage = jwt.Compile();
            }

            /*
             * During a server negiation, the client makes an intial get request to the cache endpoint
             * and passes some client negiation terms as a signed message to the server. The server then
             * validates these values and returns a signed jwt with the server negiation terms. 
             * 
             * The response from the server is essentailly the 'access token'  
             */

            RestRequest negotation = new(server.ConnectEndpoint, Method.Get);
            //Set the jwt auth header for negotiation
            negotation.AddHeader("Authorization", jwtMessage);
            negotation.AddHeader("Accept", HttpHelpers.GetContentTypeString(ContentType.Text));

            client.LogDebug("Negotiating with cache server");
            
            string authToken;
            
            //rent client
            using (ClientContract clientContract = ClientPool.Lease())
            {
                //Execute the request
                RestResponse response = await clientContract.Resource.ExecuteGetAsync(negotation, token);

                response.ThrowIfError();

                if (response.Content == null)
                {
                    throw new FBMServerNegiationException("Failed to negotiate with the server, no response");
                }
                
                //Raw content
                authToken = response.Content;
            }

            //Parse the jwt
            using (JsonWebToken jwt = JsonWebToken.Parse(authToken))
            {
                //Verify the jwt
                if (!config.VerifyCache(jwt))
                {
                    throw new SecurityException("Failed to verify the cache server's negotiation message, cannot continue");
                }

                //Confirm the server's buffer configuration
                 ValidateServerNegotation(client, challenge, jwt);
            }
            
            client.LogDebug("Server negotiation validated, connecting to server");

            //The client authorization header is the exact response
            client.ClientSocket.Headers[HttpRequestHeader.Authorization] = authToken;

            //Compute the signature of the upgrade token
            client.ClientSocket.Headers[X_UPGRADE_SIG_HEADER] = GetBase64UpgradeSingature(authToken, config.SigningKey!);

            //Check to see if adversize self is enabled
            if (cnc?.BroadcastAdverisment == true)
            {
                //Set advertisment header
                client.ClientSocket.Headers[X_NODE_DISCOVERY_HEADER] = GetAdvertismentHeader(cnc);
            }

            //Connect async
            await client.ConnectAsync(uriBuilder.Uri, token);
        }

        private static void ValidateServerNegotation(FBMClient client, string challenge, JsonWebToken jwt)
        {
            try
            {
                //Get the response message to verify the challenge, and client arguments
                using JsonDocument doc = jwt.GetPayload();

                IReadOnlyDictionary<string, JsonElement> args = doc.RootElement
                                                            .EnumerateObject()
                                                            .ToDictionary(static k => k.Name, static v => v.Value);

                //get the challenge response
                string challengeResponse = args["chl"].GetString()!;

                //Check the challenge response
                if (!challenge.Equals(challengeResponse, StringComparison.Ordinal))
                {
                    throw new FBMServerNegiationException("Failed to negotiate with the server, challenge response does not match");
                }

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

        private static string GetBase64UpgradeSingature(string? token, ReadOnlyJsonWebKey key)
        {
            //try to get the ecdsa key first
            using ECDsa? ec = key.GetECDsaPrivateKey();

            if(ec != null)
            {
                //Compute hash of the token
                byte[] hash = ManagedHash.ComputeHash(token, HashAlg.SHA256);

                //Sign the hash
                byte[] sig = ec.SignHash(hash, DSASignatureFormat.IeeeP1363FixedFieldConcatenation);

                //Return the base64 string
                return Convert.ToBase64String(sig);
            }

            //Check rsa next
            using RSA? rsa = key.GetRSAPrivateKey();
            if(rsa != null)
            {
                //Compute hash of the token
                byte[] hash = ManagedHash.ComputeHash(token, HashAlg.SHA256);

                //Sign the hash
                byte[] sig = rsa.SignHash(hash, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);

                //Return the base64 string
                return Convert.ToBase64String(sig);
            }

            throw new CryptographicException("Cache JKW does not export a supported private key for upgrade challenges");
        }

        /// <summary>
        /// Verifies the signed auth token against the given verification key
        /// </summary>
        /// <param name="signature">The base64 signature of the token</param>
        /// <param name="token">The raw token to compute the hash of</param>
        /// <param name="nodeConfig">The node configuration</param>
        /// <returns>True if the singature matches, false otherwise</returns>
        /// <exception cref="CryptographicException"></exception>
        public static bool VerifyUpgradeToken(this CacheClientConfiguration nodeConfig, string signature, string token)
        {
            return VerifyUpgradeToken(signature, token, nodeConfig.VerificationKey);
        }

        /// <summary>
        /// Verifies the signed auth token against the given verification key
        /// </summary>
        /// <param name="signature">The base64 signature of the token</param>
        /// <param name="token">The raw token to compute the hash of</param>
        /// <param name="verifcationKey">The key used to verify the singature with</param>
        /// <returns>True if the singature matches, false otherwise</returns>
        /// <exception cref="ArgumentNullException"></exception>
        /// <exception cref="CryptographicException"></exception>
        public static bool VerifyUpgradeToken(string signature, string token, ReadOnlyJsonWebKey verifcationKey)
        {
            _ = verifcationKey ?? throw new ArgumentNullException(nameof(verifcationKey));

            //get the hash of the token
            byte[] hash = ManagedHash.ComputeHash(token, HashAlg.SHA256);

            //decode the signature
            byte[] sig = Convert.FromBase64String(signature);

            //try to get the ecdsa key first
            using ECDsa? ec = verifcationKey.GetECDsaPublicKey();
            if(ec != null)
            {
                //Verify the signature
                return ec.VerifyHash(hash, sig, DSASignatureFormat.IeeeP1363FixedFieldConcatenation);
            }

            //Check rsa next
            using RSA? rsa = verifcationKey.GetRSAPublicKey();
            if(rsa != null)
            {
                //Verify the signature
                return rsa.VerifyHash(hash, sig, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);
            }

            throw new CryptographicException("Cache JKW does not export a supported public key for upgrade challenges");
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
            jwt.WriteHeader(nodeConfiguration.GetJwtHeader());

            jwt.InitPayloadClaim()
                .AddClaim("nonce", RandomHash.GetRandomBase32(16))
                .AddClaim("iat", DateTimeOffset.UtcNow.ToUnixTimeSeconds())
                .AddClaim("iss", nodeConfiguration.NodeId!)
                .AddClaim("url", nodeConfiguration.ConnectEndpoint!.ToString())
                //Optional discovery endpoint
                .AddClaim("dis", nodeConfiguration.DiscoveryEndpoint?.ToString() ?? string.Empty)
                .CommitClaims();

            //Sign message
            nodeConfiguration.SignJwt(jwt);

            return jwt.Compile();
        }

        /// <summary>
        /// Verifies the peer advertisment message
        /// </summary>
        /// <param name="config"></param>
        /// <param name="message">The advertisment message to verify</param>
        /// <returns>The advertisment message if successfully verified, or null otherwise</returns>
        /// <exception cref="FormatException"></exception>
        public static ICachePeerAdvertisment? VerifyPeerAdvertisment(this ICacheJwtManager config, string message)
        {
            using JsonWebToken jwt = JsonWebToken.Parse(message);

            //Verify the signature
            if (!config.VerifyCache(jwt))
            {
                return null;
            }

            //Get the payload
            return jwt.GetPayload<Advertisment>();
        }
        

        /// <summary>
        /// Selects a random server from a collection of active servers
        /// </summary>
        /// <param name="servers"></param>
        /// <returns>A server selected at random</returns>
        public static ICachePeerAdvertisment SelectRandom(this ICollection<ICachePeerAdvertisment> servers)
        {
            //select random server
            int randServer = RandomNumberGenerator.GetInt32(0, servers.Count);
            return servers.ElementAt(randServer);
        }


        private class Advertisment : ICachePeerAdvertisment
        {
            [JsonIgnore]
            public Uri? ConnectEndpoint { get; set; }

            [JsonIgnore]
            public Uri? DiscoveryEndpoint { get; set; }

            [JsonPropertyName("iss")]
            public string NodeId { get; set; }

            [JsonPropertyName("url")]
            public string? url
            {
                get => ConnectEndpoint?.ToString();
                set => ConnectEndpoint = value == null ? null : new Uri(value);
            }

            [JsonPropertyName("dis")]
            public string? dis
            {
                get => DiscoveryEndpoint?.ToString();
                set => DiscoveryEndpoint = value == null ? null : new Uri(value);
            }
        }
    }
}
