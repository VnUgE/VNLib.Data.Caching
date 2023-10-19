/*
* Copyright (c) 2023 Vaughn Nugent
* 
* Library: VNLib
* Package: ObjectCacheServer
* File: PeerDiscoveryEndpoint.cs 
*
* PeerDiscoveryEndpoint.cs is part of ObjectCacheServer which is part of the larger 
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
using System.Linq;
using System.Text.Json;

using VNLib.Hashing.IdentityUtility;
using VNLib.Plugins;
using VNLib.Plugins.Essentials;
using VNLib.Plugins.Essentials.Endpoints;
using VNLib.Plugins.Essentials.Extensions;
using VNLib.Plugins.Extensions.Loading;
using VNLib.Data.Caching.Extensions.Clustering;
using VNLib.Data.Caching.ObjectCache.Server.Clustering;

namespace VNLib.Data.Caching.ObjectCache.Server.Endpoints
{
    internal sealed class PeerDiscoveryEndpoint : ResourceEndpointBase
    {
        private readonly IPeerMonitor PeerMonitor;
        private readonly NodeConfig Config;

        //Loosen up protection settings
        ///<inheritdoc/>
        protected override ProtectionSettings EndpointProtectionSettings { get; } = new()
        {
            DisableSessionsRequired = true
        };

        public PeerDiscoveryEndpoint(PluginBase plugin)
        {
            //Get the peer monitor
            PeerMonitor = plugin.GetOrCreateSingleton<CachePeerMonitor>();

            //Get the node config
            Config = plugin.GetOrCreateSingleton<NodeConfig>();

            InitPathAndLog(Config.DiscoveryPath, plugin.Log);
        }

        protected override VfReturnType Get(HttpEntity entity)
        {
            //Get auth token
            string? authToken = entity.Server.Headers[HttpRequestHeader.Authorization];

            if(string.IsNullOrWhiteSpace(authToken))
            {
                entity.CloseResponse(HttpStatusCode.Unauthorized);
                return VfReturnType.VirtualSkip;
            }
          
            string subject = string.Empty;
            string challenge = string.Empty;

            //Parse auth token
            using(JsonWebToken jwt = JsonWebToken.Parse(authToken))
            {
                //try to verify against cache node first
                if (!Config.KeyStore.VerifyJwt(jwt, true))
                {
                    //failed...

                    //try to verify against client key
                    if (!Config.KeyStore.VerifyJwt(jwt, false))
                    {
                        //invalid token
                        entity.CloseResponse(HttpStatusCode.Unauthorized);
                        return VfReturnType.VirtualSkip;
                    }
                }

                using JsonDocument payload = jwt.GetPayload();

                //Get client info to pass back
                subject = payload.RootElement.TryGetProperty("sub", out JsonElement subEl) ? subEl.GetString() ?? string.Empty : string.Empty;                
                challenge = payload.RootElement.GetProperty("chl").GetString() ?? string.Empty;
            }

            //Valid key, get peer list to send to client
            CacheNodeAdvertisment[] peers = PeerMonitor.GetAllPeers()
                                        .Where(static p => p.Advertisment != null)
                                        .Select(static p => p.Advertisment!)
                                        .ToArray();

            //Build response jwt
            using JsonWebToken response = new();
            
            //set header from cache config
            response.WriteHeader(Config.KeyStore.GetJwtHeader());

            response.InitPayloadClaim()
                .AddClaim("iss", Config.Config.NodeId)
                //Audience is the requestor id
                .AddClaim("sub", subject)
                .AddClaim("iat", entity.RequestedTimeUtc.ToUnixTimeSeconds())
                //Send all peers as a json array
                .AddClaim("peers", peers)
                //Send the challenge back
                .AddClaim("chl", challenge)
                .CommitClaims();

            //Sign the response
            Config.KeyStore.SignJwt(response);
            
            //Send response to client
            entity.CloseResponse(HttpStatusCode.OK, Net.Http.ContentType.Text, response.DataBuffer);
            return VfReturnType.VirtualSkip;
        }
    }
}
