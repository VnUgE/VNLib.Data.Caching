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

        private static readonly RestClientPool ClientPool = new(2,new RestClientOptions()
        {
            MaxTimeout = 10 * 1000,
            FollowRedirects = false,
            Encoding = Encoding.UTF8,
            AutomaticDecompression = DecompressionMethods.All,
            ThrowOnAnyError = true,
        });

        private static readonly ConditionalWeakTable<FBMClient, ClientCacheConfiguration> ClientCacheConfig = new();

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
        /// Contacts the cache broker to get a list of active servers to connect to
        /// </summary>
        /// <param name="request">The request message used to connecto the broker server</param>
        /// <param name="cancellationToken">A token to cancel the operationS</param>
        /// <returns>The list of active servers</returns>
        /// <exception cref="SecurityException"></exception>
        /// <exception cref="ArgumentNullException"></exception>
        public static async Task<ActiveServer[]?> ListServersAsync(ListServerRequest request, CancellationToken cancellationToken = default)
        {
            _ = request ?? throw new ArgumentNullException(nameof(request));

            string jwtBody;
            //Build request jwt
            using (JsonWebToken requestJwt = new())
            {
                requestJwt.WriteHeader(request.JwtHeader);
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
            RestRequest listRequest = new(request.BrokerAddress, Method.Post);

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
            if (!request.VerifyJwt(responseJwt))
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
            //Request from config
            using ListServerRequest req = ListServerRequest.FromConfig(conf);
            //List servers async
            return conf.CacheServers = await ListServersAsync(req, token);
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
            _ = client ?? throw new ArgumentNullException(nameof(client));
            _ = server ?? throw new ArgumentNullException(nameof(server));
            
            //Get stored config
            ClientCacheConfiguration conf = ClientCacheConfig.GetOrCreateValue(client);
            //Connect to server (no server id because client not replication server)
            return ConnectToCacheAsync(client, conf, server, token);
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
                jwt.WriteHeader(request.JwtHeader);
                
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
                request.SignJwt(jwt);

                //Compile to string
                jwtMessage = jwt.Compile();
            }

            RestRequest negotation = new(serverUri, Method.Get);
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
                if (!request.VerifyCache(jwt))
                {
                    throw new SecurityException("Failed to verify the cache server's negotiation message, cannot continue");
                }

                //Confirm the server's buffer configuration
                ValidateServerNegotation(client, request.ServerChallenge, jwt);
            }
            
            client.LogDebug("Server negotiation validated, connecting to server");

            //The client authorization header is the exact response
            client.ClientSocket.Headers[HttpRequestHeader.Authorization] = authToken;

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
