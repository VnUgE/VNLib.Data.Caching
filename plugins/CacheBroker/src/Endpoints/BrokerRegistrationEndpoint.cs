/*
* Copyright (c) 2023 Vaughn Nugent
* 
* Library: VNLib
* Package: CacheBroker
* File: BrokerRegistrationEndpoint.cs 
*
* BrokerRegistrationEndpoint.cs is part of CacheBroker which is part of the larger 
* VNLib collection of libraries and utilities.
*
* CacheBroker is free software: you can redistribute it and/or modify 
* it under the terms of the GNU Affero General Public License as 
* published by the Free Software Foundation, either version 3 of the
* License, or (at your option) any later version.
*
* CacheBroker is distributed in the hope that it will be useful,
* but WITHOUT ANY WARRANTY; without even the implied warranty of
* MERCHANTABILITY or FITNESS FOR A PARTICULAR PURPOSE. See the
* GNU Affero General Public License for more details.
*
* You should have received a copy of the GNU Affero General Public License
* along with this program.  If not, see https://www.gnu.org/licenses/.
*/

using System;
using System.IO;
using System.Linq;
using System.Text;
using System.Net;
using System.Net.Http;
using System.Text.Json;
using System.Threading;
using System.Net.Sockets;
using System.Threading.Tasks;
using System.Collections.Generic;
using System.Text.Json.Serialization;

using RestSharp;

using VNLib.Net.Http;
using VNLib.Utils.IO;
using VNLib.Utils.Memory;
using VNLib.Utils.Logging;
using VNLib.Utils.Extensions;
using VNLib.Hashing.IdentityUtility;
using VNLib.Plugins.Essentials;
using VNLib.Plugins.Essentials.Endpoints;
using VNLib.Plugins.Essentials.Extensions;
using VNLib.Plugins.Extensions.Loading;
using VNLib.Plugins.Extensions.Loading.Events;
using VNLib.Plugins.Extensions.Loading.Routing;
using VNLib.Net.Rest.Client;

#nullable enable

namespace VNLib.Plugins.Cache.Broker.Endpoints
{
    [ConfigurationName("broker_endpoint")]
    public sealed class BrokerRegistrationEndpoint : ResourceEndpointBase, IDisposable
    {
        const string HEARTBEAT_PATH = "/heartbeat";

        //Client pool is instance based and may be disposed when the plugin is unloaded
        private readonly RestClientPool ClientPool = new(10,new RestClientOptions()
        {
            Encoding = Encoding.UTF8,
            FollowRedirects = false,
            MaxTimeout = 10 * 1000,
            ThrowOnAnyError = true
        }, null);
      

        //Matches the json schema set by the FBM caching extensions library
        private class ActiveServer
        {
            [JsonIgnore]
            public IPAddress? ServerIp { get; set; }
            [JsonPropertyName("address")]
            public Uri? Address { get; set; }
            [JsonPropertyName("server_id")]
            public string? ServerId { get; init; }
            [JsonPropertyName("ip_address")]
            public string? Ip => ServerIp?.ToString();
            [JsonIgnore]
            public string? Token { get; init; }
        }


        private readonly object ListLock;
        private readonly Dictionary<string, ActiveServer> ActiveServers;

        //Loosen up protection settings since this endpoint is not desinged for browsers or sessions
        protected override ProtectionSettings EndpointProtectionSettings { get; } = new()
        {
            DisableBrowsersOnly = true,
            DisableCrossSiteDenied = true,
            DisableSessionsRequired = true,
            DisableVerifySessionCors = true,
        };

        public BrokerRegistrationEndpoint(PluginBase plugin, IConfigScope config)
        {
            string? path = config["path"].GetString();

            InitPathAndLog(path, plugin.Log);

            ListLock = new();
            ActiveServers = new();
        }

        private async Task<ReadOnlyJsonWebKey> GetClientPublic()
        {
            return await this.GetPlugin().TryGetSecretAsync("client_public_key").ToJsonWebKey() ?? throw new InvalidOperationException("Client public key not found in vault");
        }

        private async Task<ReadOnlyJsonWebKey> GetCachePublic()
        {
            using SecretResult secret = await this.GetPlugin().TryGetSecretAsync("cache_public_key") ?? throw new InvalidOperationException("Cache public key not found in vault");
            return secret.GetJsonWebKey();
        }

        private async Task<ReadOnlyJsonWebKey> GetBrokerCertificate()
        {
            using SecretResult secret = await this.GetPlugin().TryGetSecretAsync("broker_private_key") ?? throw new InvalidOperationException("Broker private key not found in vault");
            return secret.GetJsonWebKey();
        }

        /*
         * Clients and servers use the post method to discover cache 
         * server nodes
         */

        protected override async ValueTask<VfReturnType> PostAsync(HttpEntity entity)
        {
            //Parse jwt
            using JsonWebToken? jwt = await entity.ParseFileAsAsync(ParseJwtAsync);

            if(jwt == null)
            {
                return VfReturnType.BadRequest;
            }

            //Verify with the client's pub key
            using (ReadOnlyJsonWebKey cpCert = await GetClientPublic())
            {
                if (jwt.VerifyFromJwk(cpCert))
                {
                    goto Accepted;
                }
            }

            //Accept usng the cache server key
            using (ReadOnlyJsonWebKey cacheCert = await GetCachePublic())
            {
                if (jwt.VerifyFromJwk(cacheCert))
                {
                    goto Accepted;
                }
            }

            entity.CloseResponse(HttpStatusCode.Unauthorized);
            return VfReturnType.VirtualSkip;

        Accepted:
          
            try
            {
                //Get all active servers
                ActiveServer[] servers;
                lock (ListLock)
                {
                    servers = ActiveServers.Values.ToArray();
                }
                using ReadOnlyJsonWebKey bpCert = await GetBrokerCertificate();
                
                //Create response payload with list of active servers and sign it
                using JsonWebToken response = new();
                response.WriteHeader(bpCert.JwtHeader);
                response.InitPayloadClaim(1)
                    .AddClaim("servers", servers)
                    .CommitClaims();

                //Sign the jwt using the broker key
                response.SignFromJwk(bpCert);                
                
                entity.CloseResponse(HttpStatusCode.OK, ContentType.Text, response.DataBuffer);
                return VfReturnType.VirtualSkip;
            }
            catch (KeyNotFoundException)
            {
                entity.CloseResponse(HttpStatusCode.UnprocessableEntity);
                return VfReturnType.VirtualSkip;
            }
        }

        /*
         * Server's call the put method to register or update their registration
         * for availability
         */

        private static async ValueTask<JsonWebToken?> ParseJwtAsync(Stream inputStream)
        {
            //get a buffer to store data in
            using VnMemoryStream buffer = new();
            
            //Copy input stream to buffer
            await inputStream.CopyToAsync(buffer, 4096, MemoryUtil.Shared);
            
            //Parse jwt
            return JsonWebToken.ParseRaw(buffer.AsSpan());
        }

        protected override async ValueTask<VfReturnType> PutAsync(HttpEntity entity)
        {
            //Parse jwt
            using JsonWebToken? jwt = await entity.ParseFileAsAsync(ParseJwtAsync);

            if(jwt == null)
            {
                return VfReturnType.BadRequest;
            }

            //Only cache servers may update the list
            using (ReadOnlyJsonWebKey cpCert = await GetCachePublic())
            {
                //Verify the jwt
                if (!jwt.VerifyFromJwk(cpCert))
                {
                    entity.CloseResponse(HttpStatusCode.Unauthorized);
                    return VfReturnType.VirtualSkip;
                }
            }
            
            try
            {                
                //Get message body
                using JsonDocument requestBody = jwt.GetPayload();
                
                //Get request keys
                string? serverId = requestBody.RootElement.GetProperty("sub").GetString();
                string? hostname = requestBody.RootElement.GetProperty("address").GetString();
                string? token = requestBody.RootElement.GetProperty("token").GetString();

                if (string.IsNullOrWhiteSpace(serverId) || string.IsNullOrWhiteSpace(hostname))
                {
                    entity.CloseResponse(HttpStatusCode.UnprocessableEntity);
                    return VfReturnType.VirtualSkip;
                }
                
                //Build the hostname uri
                Uri serverUri = new(hostname);
                
                //Check hostname
                if (Uri.CheckHostName(serverUri.DnsSafeHost) != UriHostNameType.Dns)
                {
                    entity.CloseResponse(HttpStatusCode.UnprocessableEntity);
                    return VfReturnType.VirtualSkip;
                }
                
                //Check dns-ip resolution to the current connection if not local connection
                if (!entity.Server.IsLoopBack())
                {
                    //Resolve the ip address of the server's hostname to make sure its ip-address matches
                    IPHostEntry remoteHost = await Dns.GetHostEntryAsync(serverUri.DnsSafeHost);
                    //See if the dns lookup resolved the same ip address that connected to the server
                    bool isAddressMatch = (from addr in remoteHost.AddressList
                                           where addr.Equals(entity.TrustedRemoteIp)
                                           select addr)
                                          .Any();
                    //If the lookup fails, exit with forbidden
                    if (!isAddressMatch)
                    {
                        entity.CloseResponse(HttpStatusCode.Forbidden);
                        return VfReturnType.VirtualSkip;
                    }
                }
                
                //Server is allowed to be put into an active state
                ActiveServer server = new()
                {
                    Address = serverUri,
                    ServerIp = entity.TrustedRemoteIp,
                    ServerId = serverId,
                    Token = token
                };
                
                //Store/update active server
                lock (ListLock)
                {
                    ActiveServers[serverId] = server;
                }
                
                Log.Debug("Server {s}:{ip} added ", serverId, entity.TrustedRemoteIp);
                //Send the broker public key used to verify authenticating clients
                entity.CloseResponse(HttpStatusCode.OK);
                return VfReturnType.VirtualSkip;
            }
            catch (KeyNotFoundException)
            {
                entity.CloseResponse(HttpStatusCode.UnprocessableEntity);
                return VfReturnType.VirtualSkip;
            }
            catch (InvalidOperationException)
            {
                entity.CloseResponse(HttpStatusCode.BadRequest);
                return VfReturnType.VirtualSkip;
            }
            catch (UriFormatException)
            {
                entity.CloseResponse(HttpStatusCode.UnprocessableEntity);
                return VfReturnType.VirtualSkip;
            }
            catch (FormatException)
            {
                entity.CloseResponse(HttpStatusCode.BadRequest);
                return VfReturnType.BadRequest;
            }
            catch (JsonException)
            {
                entity.CloseResponse(HttpStatusCode.BadRequest);
                return VfReturnType.BadRequest;
            }
        }
       

        /*
         * Schedule heartbeat interval
         */
        [ConfigurableAsyncInterval("heartbeat_sec", IntervalResultionType.Seconds)]
        public async Task OnIntervalAsync(ILogProvider log, CancellationToken pluginExit)
        {
            ActiveServer[] servers;
            //Get the current list of active servers
            lock (ListLock)
            {
                servers = ActiveServers.Values.ToArray();
            }

            using ReadOnlyJsonWebKey jwk = await GetBrokerCertificate();

            LinkedList<Task> all = new();
            //Run keeplaive request for all active servers
            foreach (ActiveServer server in servers)
            {
                all.AddLast(RunHeartbeatAsync(server, jwk));
            }
            
            //Wait for all to complete
            await Task.WhenAll(all);
        }
        
        private async Task RunHeartbeatAsync(ActiveServer server, ReadOnlyJsonWebKey privKey)
        {
            try
            {
                //build httpuri
                UriBuilder uri = new(server.Address!)
                {
                    Path = HEARTBEAT_PATH
                };
                
                string authMessage;
                //Init jwt for signing auth messages
                using (JsonWebToken jwt = new())
                {
                    jwt.WriteHeader(privKey.JwtHeader);
                    jwt.InitPayloadClaim()
                        .AddClaim("token", server.Token)
                        .CommitClaims();
                  
                    //Sign the jwt
                    jwt.SignFromJwk(privKey);
                    
                    //compile
                    authMessage = jwt.Compile();
                }
                
                //Build keeplaive request
                RestRequest keepaliveRequest = new(uri.Uri, Method.Get);
                //Add authorization token
                keepaliveRequest.AddHeader("Authorization", authMessage);

                //Rent client from pool
                using ClientContract client = ClientPool.Lease();
                //Exec
                RestResponse response = await client.Resource.ExecuteAsync(keepaliveRequest);
                //If the response was successful, then keep it in the list, if the response fails, 
                if (response.IsSuccessful)
                {
                    return;
                }
                //Remove the server
            }
            catch (HttpRequestException re) when (re.InnerException is SocketException)
            {
                Log.Debug("Server {s} removed, failed to connect", server.ServerId);
            }
            catch (TimeoutException)
            {
                Log.Information("Server {s} removed from active list due to a connection timeout", server.ServerId);
            }
            catch (Exception ex)
            {
                Log.Information("Server {s} removed from active list due to failed heartbeat request", server.ServerId);
                Log.Debug(ex);
            }
            //Remove server from active list
            lock (ListLock)
            {
                _ = ActiveServers.Remove(server.ServerId!);
            }
        }

        
        ///<inheritdoc/>
        public void Dispose()
        {
            //Cleanup client pool when exiting
            ClientPool.Dispose();
            GC.SuppressFinalize(this);
        }
    }
}
