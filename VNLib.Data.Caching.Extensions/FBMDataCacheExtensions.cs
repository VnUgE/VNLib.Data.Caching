/*
* Copyright (c) 2022 Vaughn Nugent
* 
* Library: VNLib
* Package: VNLib.Data.Caching.Extensions
* File: FBMDataCacheExtensions.cs 
*
* FBMDataCacheExtensions.cs is part of VNLib.Data.Caching.Extensions which is part of the larger 
* VNLib collection of libraries and utilities.
*
* VNLib.Data.Caching.Extensions is free software: you can redistribute it and/or modify 
* it under the terms of the GNU General Public License as published
* by the Free Software Foundation, either version 2 of the License,
* or (at your option) any later version.
*
* VNLib.Data.Caching.Extensions is distributed in the hope that it will be useful,
* but WITHOUT ANY WARRANTY; without even the implied warranty of
* MERCHANTABILITY or FITNESS FOR A PARTICULAR PURPOSE. See the GNU 
* General Public License for more details.
*
* You should have received a copy of the GNU General Public License 
* along with VNLib.Data.Caching.Extensions. If not, see http://www.gnu.org/licenses/.
*/

using System;
using System.Net;
using System.Text;
using System.Security;
using System.Text.Json;
using System.Security.Cryptography;
using System.Runtime.CompilerServices;

using RestSharp;

using VNLib.Utils.Memory;
using VNLib.Utils.Logging;
using VNLib.Utils.Extensions;
using VNLib.Hashing;
using VNLib.Hashing.IdentityUtility;
using VNLib.Net.Http;
using VNLib.Net.Rest.Client;
using VNLib.Net.Messaging.FBM.Client;
using VNLib.Net.Messaging.FBM;

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

        private static readonly IReadOnlyDictionary<string, string> BrokerJwtHeader = new Dictionary<string, string>()
        {
            { "alg", "ES384" }, //Must match alg name
            { "typ", "JWT"}
        };       

        private static readonly RestClientPool ClientPool = new(2,new RestClientOptions()
        {
            MaxTimeout = 10 * 1000,
            FollowRedirects = false,
            Encoding = Encoding.UTF8,
            AutomaticDecompression = DecompressionMethods.All,
            ThrowOnAnyError = true,
        });

        /// <summary>
        /// The default hashing algorithm used to sign an verify connection
        /// tokens
        /// </summary>
        public static readonly HashAlgorithmName CacheJwtAlgorithm = HashAlgorithmName.SHA384;

        //using the es384 algorithm for signing (friendlyname is secp384r1)
        /// <summary>
        /// The default ECCurve used by the connection library
        /// </summary>
        public static readonly ECCurve CacheCurve =  ECCurve.CreateFromFriendlyName("secp384r1");

        /// <summary>
        /// Gets a <see cref="FBMClientConfig"/> preconfigured object caching
        /// protocl
        /// </summary>
        /// <param name="heap">The client buffer heap</param>
        /// <param name="maxMessageSize">The maxium message size (in bytes)</param>
        /// <param name="debugLog">An optional debug log</param>
        /// <returns>A preconfigured <see cref="FBMClientConfig"/> for object caching</returns>
        public static FBMClientConfig GetDefaultConfig(IUnmangedHeap heap, int maxMessageSize, ILogProvider? debugLog = null)
        {
            return new()
            {
                BufferHeap = heap,
                MaxMessageSize = maxMessageSize * 2,
                RecvBufferSize = maxMessageSize,
                MessageBufferSize = maxMessageSize,

                MaxHeaderBufferSize = MAX_FBM_MESSAGE_HEADER_SIZE,
                SubProtocol = CACHE_WS_SUB_PROCOL,

                HeaderEncoding = Helpers.DefaultEncoding,

                KeepAliveInterval = TimeSpan.FromSeconds(30),

                DebugLog = debugLog
            };
        }


        private class CacheConnectionConfig
        {
            public ECDsa ClientAlg { get; init; }
            public ECDsa BrokerAlg { get; init; }
            public string ServerChallenge { get; init; }
            public string? NodeId { get; set; }
            public Uri? BrokerAddress { get; set; }
            public bool useTls { get; set; }
            public ActiveServer[]? BrokerServers { get; set; }

            public CacheConnectionConfig()
            {
                //Init the algorithms
                ClientAlg = ECDsa.Create(CacheCurve);
                BrokerAlg = ECDsa.Create(CacheCurve);
                ServerChallenge = RandomHash.GetRandomBase32(24);
            }

            ~CacheConnectionConfig()
            {
                ClientAlg.Clear();
                BrokerAlg.Clear();
            }
        }

        /// <summary>
        /// Contacts the cache broker to get a list of active servers to connect to
        /// </summary>
        /// <param name="brokerAddress">The broker server to connec to</param>
        /// <param name="clientPrivKey">The private key used to sign messages sent to the broker</param>
        /// <param name="brokerPubKey">The broker public key used to verify broker messages</param>
        /// <param name="cancellationToken">A token to cancel the operationS</param>
        /// <returns>The list of active servers</returns>
        /// <exception cref="SecurityException"></exception>
        /// <exception cref="ArgumentNullException"></exception>
        public static async Task<ActiveServer[]?> ListServersAsync(Uri brokerAddress, ReadOnlyMemory<byte> clientPrivKey, ReadOnlyMemory<byte> brokerPubKey, CancellationToken cancellationToken = default)
        {
            using ECDsa client = ECDsa.Create(CacheCurve);
            using ECDsa broker = ECDsa.Create(CacheCurve);

            //Import client private key
            client.ImportPkcs8PrivateKey(clientPrivKey.Span, out _);
            //Broker public key to verify broker messages
            broker.ImportSubjectPublicKeyInfo(brokerPubKey.Span, out _);

            return await ListServersAsync(brokerAddress, client, broker, cancellationToken);
        }

        /// <summary>
        /// Contacts the cache broker to get a list of active servers to connect to
        /// </summary>
        /// <param name="brokerAddress">The broker server to connec to</param>
        /// <param name="clientAlg">The signature algorithm used to sign messages to the broker</param>
        /// <param name="brokerAlg">The signature used to verify broker messages</param>
        /// <param name="cancellationToken">A token to cancel the operationS</param>
        /// <returns>The list of active servers</returns>
        /// <exception cref="SecurityException"></exception>
        /// <exception cref="ArgumentNullException"></exception>
        public static async Task<ActiveServer[]?> ListServersAsync(Uri brokerAddress, ECDsa clientAlg, ECDsa brokerAlg, CancellationToken cancellationToken = default)
        {
            _ = brokerAddress ?? throw new ArgumentNullException(nameof(brokerAddress));
            _ = clientAlg ?? throw new ArgumentNullException(nameof(clientAlg));
            _ = brokerAlg ?? throw new ArgumentNullException(nameof(brokerAlg));

            string jwtBody;
            //Build request jwt
            using (JsonWebToken requestJwt = new())
            {
                requestJwt.WriteHeader(BrokerJwtHeader);
                requestJwt.InitPayloadClaim()
                    .AddClaim("iat", DateTimeOffset.UtcNow.ToUnixTimeMilliseconds())
                    .CommitClaims();
                //sign the jwt
                requestJwt.Sign(clientAlg, in CacheJwtAlgorithm, 512);
                //Compile the jwt
                jwtBody = requestJwt.Compile();
            }
            //New list request
            RestRequest listRequest = new(brokerAddress, Method.Post);
            //Add the jwt as a string to the request body
            listRequest.AddStringBody(jwtBody, DataFormat.None);
            listRequest.AddHeader("Content-Type", HttpHelpers.GetContentTypeString(ContentType.Text));
            //Rent client
            using ClientContract client = ClientPool.Lease();
            //Exec list request
            RestResponse response = await client.Resource.ExecuteAsync(listRequest, cancellationToken);
            if (!response.IsSuccessful)
            {
                throw response.ErrorException!;
            }
            //Response is jwt
            using JsonWebToken responseJwt = JsonWebToken.ParseRaw(response.RawBytes);
            //Verify the jwt
            if (!responseJwt.Verify(brokerAlg, in CacheJwtAlgorithm))
            {
                throw new SecurityException("Failed to verify the broker's challenge, cannot continue");
            }
            using JsonDocument doc = responseJwt.GetPayload();
            return doc.RootElement.GetProperty("servers").Deserialize<ActiveServer[]>();
        }

        /// <summary>
        /// Configures a connection to the remote cache server at the specified location
        /// with proper authentication.
        /// </summary>
        /// <param name="client"></param>
        /// <param name="serverUri">The server's address</param>
        /// <param name="signingKey">The pks8 format EC private key uesd to sign the message</param>
        /// <param name="challenge">A challenge to send to the server</param>
        /// <param name="nodeId">A token used to identify the current server's event queue on the remote server</param>
        /// <param name="token">A token to cancel the connection operation</param>
        /// <param name="useTls">Enables the secure websocket protocol</param>
        /// <returns>A Task that completes when the connection has been established</returns>
        /// <exception cref="ArgumentNullException"></exception>
        public static Task ConnectAsync(this FBMClient client, string serverUri, ReadOnlyMemory<byte> signingKey, string challenge, string? nodeId, bool useTls, CancellationToken token = default)
        {
            //Sign the jwt
            using ECDsa sigAlg = ECDsa.Create(CacheCurve);
            //Import the signing key
            sigAlg.ImportPkcs8PrivateKey(signingKey.Span, out _);
            //Return without await because the alg is used to sign before this method returns and can be discarded
            return ConnectAsync(client, serverUri, sigAlg, challenge, nodeId, useTls, token);
        }

        private static Task ConnectAsync(FBMClient client, string serverUri, ECDsa sigAlg, string challenge, string? nodeId, bool useTls, CancellationToken token = default)
        {
            _ = serverUri ?? throw new ArgumentNullException(nameof(serverUri));
            _ = challenge ?? throw new ArgumentNullException(nameof(challenge));
            //build ws uri
            UriBuilder uriBuilder = new(serverUri)
            {
                Scheme = useTls ? "wss://" : "ws://"
            };
            string jwtMessage;
            //Init jwt for connecting to server
            using (JsonWebToken jwt = new())
            {
                jwt.WriteHeader(BrokerJwtHeader);
                //Init claim
                JwtPayload claim = jwt.InitPayloadClaim();
                claim.AddClaim("challenge", challenge);
                if (!string.IsNullOrWhiteSpace(nodeId))
                {
                    /*
                    * The unique node id so the other nodes know to load the 
                    * proper event queue for the current server
                    */
                    claim.AddClaim("server_id", nodeId);
                }
                claim.CommitClaims();
                
                //Sign jwt
                jwt.Sign(sigAlg, in CacheJwtAlgorithm, 512);
                
                //Compile to string
                jwtMessage = jwt.Compile();
            }
            //Set jwt as authorization header
            client.ClientSocket.Headers[HttpRequestHeader.Authorization] = jwtMessage;
            //Connect async
            return client.ConnectAsync(uriBuilder.Uri, token);
        }

        /// <summary>
        /// Registers the current server as active with the specified broker
        /// </summary>
        /// <param name="brokerAddress">The address of the broker to register with</param>
        /// <param name="signingKey">The private key used to sign the message</param>
        /// <param name="serverAddress">The local address of the current server used for discovery</param>
        /// <param name="nodeId">The unique id to identify this server (for event queues)</param>
        /// <param name="keepAliveToken">A unique security token used by the broker to authenticate itself</param>
        /// <returns>A task that resolves when a successful registration is completed, raises exceptions otherwise</returns>
        public static async Task ResgisterWithBrokerAsync(Uri brokerAddress, ReadOnlyMemory<byte> signingKey, string serverAddress, string nodeId, string keepAliveToken)
        {
            _ = brokerAddress ?? throw new ArgumentNullException(nameof(brokerAddress));
            _ = serverAddress ?? throw new ArgumentNullException(nameof(serverAddress));
            _ = keepAliveToken ?? throw new ArgumentNullException(nameof(keepAliveToken));
            _ = nodeId ?? throw new ArgumentNullException(nameof(nodeId));

            string requestData;
            //Create the jwt for signed registration message
            using (JsonWebToken jwt = new())
            {
                //Shared jwt header
                jwt.WriteHeader(BrokerJwtHeader);
                //build jwt claim
                jwt.InitPayloadClaim()
                    .AddClaim("address", serverAddress)
                    .AddClaim("server_id", nodeId)
                    .AddClaim("token", keepAliveToken)
                    .CommitClaims();

                //Sign the jwt
                using (ECDsa sigAlg = ECDsa.Create(CacheCurve))
                {
                    //Import the signing key
                    sigAlg.ImportPkcs8PrivateKey(signingKey.Span, out _);

                    jwt.Sign(sigAlg, in CacheJwtAlgorithm, 512);
                }
                //Compile and save
                requestData = jwt.Compile();
            }
            //Create reg request message
            RestRequest regRequest = new(brokerAddress);
            regRequest.AddStringBody(requestData, DataFormat.None);
            regRequest.AddHeader("Content-Type", "text/plain");
            //Rent client
            using ClientContract client = ClientPool.Lease();
            //Exec the regitration request
            RestResponse response = await client.Resource.ExecutePutAsync(regRequest);
            if(!response.IsSuccessful)
            {
                throw response.ErrorException!;
            }
        }


        private static readonly ConditionalWeakTable<FBMClient, CacheConnectionConfig> ClientCacheConfig = new();

        /// <summary>
        /// Imports the client signature algorithim's private key from its pkcs8 binary representation
        /// </summary>
        /// <param name="client"></param>
        /// <param name="pkcs8PrivateKey">Pkcs8 format private key</param>
        /// <returns>Chainable fluent object</returns>
        /// <exception cref="ArgumentException"></exception>
        /// <exception cref="CryptographicException"></exception>
        public static FBMClient ImportClientPrivateKey(this FBMClient client, ReadOnlySpan<byte> pkcs8PrivateKey)
        {
            CacheConnectionConfig conf = ClientCacheConfig.GetOrCreateValue(client);
            conf.ClientAlg.ImportPkcs8PrivateKey(pkcs8PrivateKey, out _);
            return client;
        }
        /// <summary>
        /// Imports the public key used to verify broker server messages
        /// </summary>
        /// <param name="client"></param>
        /// <param name="spkiPublicKey">The subject-public-key-info formatted broker public key</param>
        /// <returns>Chainable fluent object</returns>
        /// <exception cref="ArgumentException"></exception>
        /// <exception cref="CryptographicException"></exception>
        public static FBMClient ImportBrokerPublicKey(this FBMClient client, ReadOnlySpan<byte> spkiPublicKey)
        {
            CacheConnectionConfig conf = ClientCacheConfig.GetOrCreateValue(client);
            conf.BrokerAlg.ImportSubjectPublicKeyInfo(spkiPublicKey, out _);
            return client;
        }
        /// <summary>
        /// Specifies if all connections should be using TLS
        /// </summary>
        /// <param name="client"></param>
        /// <param name="useTls">A value that indicates if connections should use TLS</param>
        /// <returns>Chainable fluent object</returns>
        public static FBMClient UseTls(this FBMClient client, bool useTls)
        {
            CacheConnectionConfig conf = ClientCacheConfig.GetOrCreateValue(client);
            conf.useTls = useTls;
            return client;
        }
        /// <summary>
        /// Specifies the broker address to discover cache nodes from
        /// </summary>
        /// <param name="client"></param>
        /// <param name="brokerAddress">The address of the server broker</param>
        /// <returns>Chainable fluent object</returns>
        /// <exception cref="ArgumentNullException"></exception>
        public static FBMClient UseBroker(this FBMClient client, Uri brokerAddress)
        {
            CacheConnectionConfig conf = ClientCacheConfig.GetOrCreateValue(client);
            conf.BrokerAddress = brokerAddress ?? throw new ArgumentNullException(nameof(brokerAddress));
            return client;
        }

        /// <summary>
        /// Specifies the current server's cluster node id. If this 
        /// is a server connection attempting to listen for changes on the
        /// remote server, this id must be set and unique
        /// </summary>
        /// <param name="client"></param>
        /// <param name="nodeId">The cluster node id of the current server</param>
        /// <returns>Chainable fluent object</returns>
        /// <exception cref="ArgumentNullException"></exception>
        public static FBMClient SetNodeId(this FBMClient client, string nodeId)
        {
            CacheConnectionConfig conf = ClientCacheConfig.GetOrCreateValue(client);
            conf.NodeId = nodeId ?? throw new ArgumentNullException(nameof(nodeId));
            return client;
        }

        /// <summary>
        /// Discovers cache nodes in the broker configured for the current client.
        /// </summary>
        /// <param name="client"></param>
        /// <param name="token">A token to cancel the discovery</param>
        /// <returns>A task the resolves the list of active servers on the broker server</returns>
        public static Task<ActiveServer[]?> DiscoverNodesAsync(this FBMClientWorkerBase client, CancellationToken token = default)
        {
            return client.Client.DiscoverNodesAsync(token);
        }
        /// <summary>
        /// Discovers cache nodes in the broker configured for the current client.
        /// </summary>
        /// <param name="client"></param>
        /// <param name="token">A token to cancel the discovery </param>
        /// <returns>A task the resolves the list of active servers on the broker server</returns>
        public static async Task<ActiveServer[]?> DiscoverNodesAsync(this FBMClient client, CancellationToken token = default)
        {
            CacheConnectionConfig conf = ClientCacheConfig.GetOrCreateValue(client);
            //List servers async
            ActiveServer[]? servers = await ListServersAsync(conf.BrokerAddress!, conf.ClientAlg, conf.BrokerAlg, token);
            conf.BrokerServers = servers;
            return servers;
        }

        /// <summary>
        /// Connects the client to a remote cache server 
        /// </summary>
        /// <param name="client"></param>
        /// <param name="server">The server to connect to</param>
        /// <param name="token">A token to cancel the connection and/or wait operation</param>
        /// <returns>A task that resolves when cancelled or when the connection is lost to the server</returns>
        /// <exception cref="OperationCanceledException"></exception>
        public static Task ConnectAndWaitForExitAsync(this FBMClientWorkerBase client, ActiveServer server, CancellationToken token = default)
        {
            return client.Client.ConnectAndWaitForExitAsync(server, token);  
        }

        /// <summary>
        /// Connects the client to a remote cache server 
        /// </summary>
        /// <param name="client"></param>
        /// <param name="server">The server to connect to</param>
        /// <param name="token">A token to cancel the connection and/or wait operation</param>
        /// <returns>A task that resolves when cancelled or when the connection is lost to the server</returns>
        /// <exception cref="OperationCanceledException"></exception>
        public static async Task ConnectAndWaitForExitAsync(this FBMClient client, ActiveServer server, CancellationToken token = default)
        {
            CacheConnectionConfig conf = ClientCacheConfig.GetOrCreateValue(client);
            //Connect to server (no server id because client not replication server)
            await ConnectAsync(client, server.HostName!, conf.ClientAlg, conf.ServerChallenge, conf.NodeId, conf.useTls, token);
            //Get task for cancellation
            Task cancellation = token.WaitHandle.WaitAsync();
            //Task for status handle
            Task run = client.ConnectionStatusHandle.WaitAsync();
            //Wait for cancellation or 
            _ = await Task.WhenAny(cancellation, run);
            //Normal try to disconnect the socket
            await client.DisconnectAsync(CancellationToken.None);
            //Notify if cancelled
            token.ThrowIfCancellationRequested();
        }

        /// <summary>
        /// Selects a random server from a collection of active servers
        /// </summary>
        /// <param name="servers"></param>
        /// <returns>A server selected at random</returns>
        public static ActiveServer SelectRandom(this ICollection<ActiveServer> servers)
        {
            //select random server
            int randServer = RandomNumberGenerator.GetInt32(0, servers.Count);
            return servers.ElementAt(randServer);
        }
    }
}
