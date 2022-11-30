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


        /// <summary>
        /// The raw JWT message header
        /// </summary>
        public static ReadOnlyMemory<byte> JwtMessageHeader { get; } = JsonSerializer.SerializeToUtf8Bytes(BrokerJwtHeader);

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
        /// <param name="signingAlg">The signature algorithm used to sign messages to the broker</param>
        /// <param name="verificationAlg">The signature used to verify broker messages</param>
        /// <param name="cancellationToken">A token to cancel the operationS</param>
        /// <returns>The list of active servers</returns>
        /// <exception cref="SecurityException"></exception>
        /// <exception cref="ArgumentNullException"></exception>
        public static async Task<ActiveServer[]?> ListServersAsync(Uri brokerAddress, ECDsa signingAlg, ECDsa verificationAlg, CancellationToken cancellationToken = default)
        {
            _ = brokerAddress ?? throw new ArgumentNullException(nameof(brokerAddress));
            _ = signingAlg ?? throw new ArgumentNullException(nameof(signingAlg));
            _ = verificationAlg ?? throw new ArgumentNullException(nameof(verificationAlg));

            string jwtBody;
            //Build request jwt
            using (JsonWebToken requestJwt = new())
            {
                requestJwt.WriteHeader(JwtMessageHeader.Span);
                requestJwt.InitPayloadClaim()
                    .AddClaim("iat", DateTimeOffset.UtcNow.ToUnixTimeMilliseconds())
                    .CommitClaims();
                //sign the jwt
                requestJwt.Sign(signingAlg, in CacheJwtAlgorithm, 512);
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
            if (!responseJwt.Verify(verificationAlg, in CacheJwtAlgorithm))
            {
                throw new SecurityException("Failed to verify the broker's challenge, cannot continue");
            }
            using JsonDocument doc = responseJwt.GetPayload();
            return doc.RootElement.GetProperty("servers").Deserialize<ActiveServer[]>();
        }


        /// <summary>
        /// Registers the current server as active with the specified broker
        /// </summary>
        /// <param name="registration">The registration request</param>
        public static async Task ResgisterWithBrokerAsync(BrokerRegistrationRequest registration)
        {
            _ = registration.HeartbeatToken ?? throw new ArgumentException("Missing required heartbeat access token");
            _ = registration.NodeId ?? throw new ArgumentException("Missing required cache server NodeId");
            _ = registration.BrokerAddress ?? throw new ArgumentException("Broker server address has not been configured");
            _ = registration.RegistrationAddress ?? throw new ArgumentException("Missing required registration address", nameof(registration));

            string requestData;
            //Create the jwt for signed registration message
            using (JsonWebToken jwt = new())
            {
                //Shared jwt header
                jwt.WriteHeader(JwtMessageHeader.Span);
                //build jwt claim
                jwt.InitPayloadClaim()
                    .AddClaim("address", registration.RegistrationAddress)
                    .AddClaim("sub", registration.NodeId)
                    .AddClaim("token", registration.HeartbeatToken)
                    .CommitClaims();

                //Sign the jwt
                jwt.Sign(registration.SiginingAlg, in CacheJwtAlgorithm, 512);
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
            if(!response.IsSuccessful)
            {
                throw response.ErrorException!;
            }
        }


        private static readonly ConditionalWeakTable<FBMClient, ClientCacheConfiguration> ClientCacheConfig = new();

        /// <summary>
        /// Allows for configuration of an <see cref="FBMClient"/>
        /// for a connection to a cache server
        /// </summary>
        /// <param name="client"></param>
        /// <returns>A fluent api configuration builder for the current client</returns>
        public static ClientCacheConfiguration GetCacheConfiguration(this FBMClient client) => ClientCacheConfig.GetOrCreateValue(client);

        /// <summary>
        /// Discovers cache nodes in the broker configured for the current client.
        /// </summary>
        /// <param name="client"></param>
        /// <param name="token">A token to cancel the discovery</param>
        /// <returns>A task the resolves the list of active servers on the broker server</returns>
        public static Task<ActiveServer[]?> DiscoverCacheNodesAsync(this FBMClientWorkerBase client, CancellationToken token = default) => client.Client.DiscoverCacheNodesAsync(token);
        /// <summary>
        /// Discovers cache nodes in the broker configured for the current client.
        /// </summary>
        /// <param name="client"></param>
        /// <param name="token">A token to cancel the discovery </param>
        /// <returns>A task the resolves the list of active servers on the broker server</returns>
        public static async Task<ActiveServer[]?> DiscoverCacheNodesAsync(this FBMClient client, CancellationToken token = default)
        {
            ClientCacheConfiguration conf = ClientCacheConfig.GetOrCreateValue(client);
            //List servers async
            ActiveServer[]? servers = await ListServersAsync(conf.BrokerAddress!, conf.SigningKey, conf.VerificationKey, token);
            conf.CacheServers = servers;
            return servers;
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
        /// <exception cref="ArgumentException"></exception>
        public static async Task<ActiveServer> ConnectToRandomCacheAsync(this FBMClient client, CancellationToken cancellation = default)
        {
            //Get stored config
            ClientCacheConfiguration conf = ClientCacheConfig.GetOrCreateValue(client);
            //Select random
            ActiveServer? randomServer = conf.CacheServers?.SelectRandom();
            _ = randomServer ?? throw new ArgumentException("No servers detected, cannot connect");
            await ConnectToCacheAsync(client, randomServer, cancellation);
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
        public static Task ConnectToCacheAsync(this FBMClient client, ActiveServer server, CancellationToken token = default)
        {
            //Get stored config
            ClientCacheConfiguration conf = ClientCacheConfig.GetOrCreateValue(client);
            //Connect to server (no server id because client not replication server)
            return ConnectToCacheAsync(client, conf, server, token);
        }

        private static void LogDebug(this FBMClient client, string message)
        {
            client.Config.DebugLog.Debug("{debug}: {data}","[CACHE]", message);
        }

        private static async Task ConnectToCacheAsync(FBMClient client, ClientCacheConfiguration request, ActiveServer server, CancellationToken token = default)
        {
            //Construct server uri
            Uri serverUri = new(server.HostName!);
            
            //build ws uri
            UriBuilder uriBuilder = new(serverUri)
            {
                Scheme = request.UseTls ? "wss://" : "ws://"
            };
            
            string jwtMessage;
            //Init jwt for connecting to server
            using (JsonWebToken jwt = new())
            {
                jwt.WriteHeader(JwtMessageHeader.Span);
                //Init claim
                JwtPayload claim = jwt.InitPayloadClaim();
                claim.AddClaim("chl", request.ServerChallenge);
                if (!string.IsNullOrWhiteSpace(request.NodeId))
                {
                    /*
                    * The unique node id so the other nodes know to load the 
                    * proper event queue for the current server
                    */
                    claim.AddClaim("sub", request.NodeId);
                }
                claim.CommitClaims();

                //Sign jwt
                jwt.Sign(request.SigningKey, in CacheJwtAlgorithm, 512);

                //Compile to string
                jwtMessage = jwt.Compile();
            }

            RestRequest negotation = new(serverUri, Method.Get);
            //Set the jwt auth header for negotiation
            negotation.AddHeader("Authorization", jwtMessage);

            client.LogDebug("Negotiating with cache server");

            //rent client
            using (ClientContract clientContract = ClientPool.Lease())
            {
                //Execute the request
                RestResponse response = await clientContract.Resource.ExecuteAsync(negotation, token);
                //Check verify the response

                if (!response.IsSuccessful)
                {
                    throw response.ErrorException!;
                }

                if (response.Content == null)
                {
                    throw new FBMServerNegiationException("Failed to negotiate with the server, no response");
                }

                //Raw content
                string authToken = response.Content;

                //Parse the jwt
                using JsonWebToken jwt = JsonWebToken.Parse(authToken);

                //Verify the jwt
                if (!jwt.Verify(request.VerificationKey, in CacheJwtAlgorithm))
                {
                    throw new SecurityException("Failed to verify the broker's negotiation message, cannot continue");
                }

                //Confirm the server's buffer configuration
                ValidateServerNegotation(client, request.ServerChallenge, jwt);

                client.LogDebug("Server negotiation validated, connecting to server");

                //The client authorization header is the exact response
                client.ClientSocket.Headers[HttpRequestHeader.Authorization] = authToken;
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
                if (challenge.Equals(challengeResponse, StringComparison.Ordinal))
                {
                    throw new FBMServerNegiationException("Failed to negotiate with the server, challenge response does not match");
                }

                //Get the negiation values
                uint recvBufSize = args[FBMClient.REQ_RECV_BUF_QUERY_ARG].GetUInt32();
                uint headerBufSize= args[FBMClient.REQ_HEAD_BUF_QUERY_ARG].GetUInt32();
                uint maxMessSize= args[FBMClient.REQ_MAX_MESS_QUERY_ARG].GetUInt32();
                
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
