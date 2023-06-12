/*
* Copyright (c) 2023 Vaughn Nugent
* 
* Library: VNLib
* Package: ObjectCacheServer
* File: BrokerHeartBeatEndpoint.cs 
*
* BrokerHeartBeatEndpoint.cs is part of ObjectCacheServer which is part of the larger 
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
using System.Threading.Tasks;


using VNLib.Plugins;
using VNLib.Utils.Logging;
using VNLib.Plugins.Essentials;
using VNLib.Hashing.IdentityUtility;
using VNLib.Plugins.Essentials.Endpoints;
using VNLib.Plugins.Essentials.Extensions;
using VNLib.Plugins.Extensions.Loading;

namespace VNLib.Data.Caching.ObjectCache.Server
{
    internal sealed class BrokerHeartBeatEndpoint : ResourceEndpointBase
    {
        private readonly IBrokerHeartbeatNotifier _heartBeat;
        private readonly Task<IPAddress[]> BrokerIpList;
        private readonly bool DebugMode;

        ///<inheritdoc/>
        protected override ProtectionSettings EndpointProtectionSettings { get; } = new()
        {
            DisableBrowsersOnly = true,
            DisableSessionsRequired = true
        };

        public BrokerHeartBeatEndpoint(PluginBase plugin)
        {
            //Get debug flag
            DebugMode = plugin.IsDebug();

            //Get or create the current node config
            _heartBeat = plugin.GetOrCreateSingleton<NodeConfig>();

            /*
             * Resolve the ip address of the broker and store it to verify connections
             * later
             */
            BrokerIpList = Dns.GetHostAddressesAsync(_heartBeat.GetBrokerAddress().DnsSafeHost);

            //Setup endpoint
            InitPathAndLog("/heartbeat", plugin.Log);
        }
      

        protected override async ValueTask<VfReturnType> GetAsync(HttpEntity entity)
        {
            //If-not loopback then verify server address
            if (!entity.Server.IsLoopBack())
            {
                //Load and verify the broker's ip address matches with an address we have stored
                IPAddress[] addresses = await BrokerIpList;

                if (!addresses.Contains(entity.TrustedRemoteIp))
                {
                    if (DebugMode)
                    {
                        Log.Debug("Received connection {ip} that was not a DNS safe address for the broker server, access denied");
                    }

                    //Token invalid
                    entity.CloseResponse(HttpStatusCode.Forbidden);
                    return VfReturnType.VirtualSkip;
                }
            }

            //Get the authorization jwt
            string? jwtAuth = entity.Server.Headers[HttpRequestHeader.Authorization];
            
            if (string.IsNullOrWhiteSpace(jwtAuth))
            {
                //Token invalid
                entity.CloseResponse(HttpStatusCode.Forbidden);
                return VfReturnType.VirtualSkip;
            }
            
            //Parse the jwt
            using JsonWebToken jwt = JsonWebToken.Parse(jwtAuth);

            //Verify the jwt using the broker's public key certificate
            using (ReadOnlyJsonWebKey cert = _heartBeat.GetBrokerPublicKey())
            {
                //Verify the jwt
                if (!jwt.VerifyFromJwk(cert))
                {
                    //Token invalid
                    entity.CloseResponse(HttpStatusCode.Forbidden);
                    return VfReturnType.VirtualSkip;
                }
            }
           
            string? auth;
            //Recover the auth token from the jwt
            using (JsonDocument doc = jwt.GetPayload())
            {
                auth = doc.RootElement.GetProperty("token").GetString();
            }

            //Get our stored token used for registration
            string? selfToken = _heartBeat.GetAuthToken();

            //Verify token
            if (selfToken != null && selfToken.Equals(auth, StringComparison.Ordinal))
            {
                //Signal keepalive
                _heartBeat.HearbeatReceived();
                entity.CloseResponse(HttpStatusCode.OK);
                return VfReturnType.VirtualSkip;
            }

            if (DebugMode)
            {
                Log.Debug("Invalid auth token recieved from broker sever, access denied");
            }

            //Token invalid
            entity.CloseResponse(HttpStatusCode.Forbidden);
            return VfReturnType.VirtualSkip;
        }
    }
}
