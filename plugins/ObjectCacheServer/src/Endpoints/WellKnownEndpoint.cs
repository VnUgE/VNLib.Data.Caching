/*
* Copyright (c) 2024 Vaughn Nugent
* 
* Library: VNLib
* Package: ObjectCacheServer
* File: WellKnownEndpoint.cs 
*
* WellKnownEndpoint.cs is part of ObjectCacheServer which is part of the larger 
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

using VNLib.Hashing;
using VNLib.Hashing.IdentityUtility;
using VNLib.Plugins;
using VNLib.Plugins.Essentials;
using VNLib.Plugins.Essentials.Endpoints;
using VNLib.Plugins.Essentials.Extensions;
using VNLib.Plugins.Extensions.Loading;
using VNLib.Data.Caching.Extensions;
using VNLib.Data.Caching.Extensions.Clustering;

namespace VNLib.Data.Caching.ObjectCache.Server.Endpoints
{

    /*
     * The well-known endpoint is used to advertise the node's existence to 
     * the network. Clients need to know the endpoint layout to be able to
     * connect and discover other nodes.
     */
    internal sealed class WellKnownEndpoint : ResourceEndpointBase
    {       

        //Store serialized advertisment
        private readonly CacheNodeAdvertisment _advertisment;
        private readonly ICacheAuthManager _keyStore;

        //Loosen up security requirements
        protected override ProtectionSettings EndpointProtectionSettings { get; } = new()
        {
            DisableSessionsRequired = true,
        };

        public WellKnownEndpoint(PluginBase plugin)
        {
            //Get the node config
            NodeConfig nodeConfig = plugin.GetOrCreateSingleton<ObjectCacheSystemState>().Configuration;

            //serialize the config, discovery may not be enabled
            _advertisment = nodeConfig.Config.Advertisment;
            _keyStore = nodeConfig.KeyStore;

            InitPathAndLog(nodeConfig.WellKnownPath, plugin.Log);
        }

        protected override VfReturnType Get(HttpEntity entity)
        {
            string entropy = RandomHash.GetRandomBase32(16);

            //Create jwt signed for the client
            using JsonWebToken jwt = new();
            
            jwt.WriteHeader(_keyStore.GetJwtHeader());
            //Write the advertisment as the message body
            jwt.InitPayloadClaim()
                .AddClaim("sub", _advertisment)
                .AddClaim("chl", entropy)
                .AddClaim("iat", entity.RequestedTimeUtc.ToUnixTimeSeconds())
                .CommitClaims();

            _keyStore.SignJwt(jwt);

            //Return the advertisment jwt
            entity.CloseResponse(HttpStatusCode.OK, Net.Http.ContentType.Text, jwt.DataBuffer);
            return VfReturnType.VirtualSkip;
        }
    }
}
