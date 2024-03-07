/*
* Copyright (c) 2024 Vaughn Nugent
* 
* Library: VNLib
* Package: ObjectCacheServer
* File: CacheNegotationManager.cs 
*
* CacheNegotationManager.cs is part of ObjectCacheServer which is part of the larger 
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

using VNLib.Hashing;
using VNLib.Hashing.IdentityUtility;
using VNLib.Net.Messaging.FBM.Client;
using VNLib.Plugins;
using VNLib.Plugins.Extensions.Loading;
using VNLib.Data.Caching.Extensions;
using VNLib.Data.Caching.ObjectCache.Server.Cache;

namespace VNLib.Data.Caching.ObjectCache.Server.Endpoints
{
    internal class ClientNegotiationState
    {
        public string? Challenge { get; set; }

        public string? NodeId { get; set; }

        public bool IsPeer { get; set; }
    }

    internal sealed class CacheNegotationManager(PluginBase plugin)
    {
        /*
         * Cache keys are centralized and may be shared between all cache server nodes. This means
         * that any client would be able to get a signed negotiation from any server and use it to
         * upgrade a connection to any other server. This is property is to be avoided because servers
         * may have different configurations that a malicious client could exploit. To prevent that
         * a unique server generated Audience ID is used in the negotiation token and verified when
         * an upgrade is requested. This ensures that the client is connecting to the same server
         * that issued the negotiation token.
         * 
         * With this operational theory, someone has to expose their buffer configuration. At the moment
         * I think it would be best for servers to keep their buffer configuration private, as it could
         * cause more damage to the network. This is not really a protection measure because a malicious
         * client could use trial and error to find the servers buffer configuration. 
         */

        private static readonly TimeSpan AuthTokenExpiration = TimeSpan.FromSeconds(30);

        private readonly string AudienceLocalServerId = Guid.NewGuid().ToString("N");

        private readonly ObjectCacheSystemState _sysState = plugin.GetOrCreateSingleton<ObjectCacheSystemState>();

        private NodeConfig NodeConfig => _sysState.Configuration;

        private CacheMemoryConfiguration CacheConfig => _sysState.MemoryConfiguration;

        public bool IsClientNegotiationValid(string authToken, out ClientNegotiationState state)
        {
            state = new();

            // Parse jwt
            using JsonWebToken jwt = JsonWebToken.Parse(authToken);

            //verify signature for client
            if (NodeConfig.KeyStore.VerifyJwt(jwt, false))
            {
                //Validated as normal client
            }
            //May be signed by a cache server
            else if (NodeConfig.KeyStore.VerifyJwt(jwt, true))
            {
                //Set peer and verified flag since the another cache server signed the request
                state.IsPeer = true;
            }
            else
            {
                return false;
            }

            //Recover json body
            using JsonDocument doc = jwt.GetPayload();

            if (doc.RootElement.TryGetProperty("sub", out JsonElement servIdEl))
            {
                state.NodeId = servIdEl.GetString();
            }

            //Challenge is required
            state.Challenge = doc.RootElement.GetProperty("chl").GetString()!;           

            return true;
        }

        public JsonWebToken ConfirmClientNegotiation(ClientNegotiationState state, IPAddress clientIp, DateTimeOffset now)
        {
            //Verified, now we can create an auth message with a short expiration
            JsonWebToken auth = new();

            auth.WriteHeader(NodeConfig.KeyStore.GetJwtHeader());
            auth.InitPayloadClaim()
                .AddClaim("aud", AudienceLocalServerId)
                .AddClaim("iat", now.ToUnixTimeSeconds())
                .AddClaim("exp", now.Add(AuthTokenExpiration).ToUnixTimeSeconds())
                .AddClaim("nonce", RandomHash.GetRandomBase32(8))
                .AddClaim("chl", state.Challenge!)
                //Set the ispeer flag if the request was signed by a cache server
                .AddClaim("isPeer", state.IsPeer)
                //Specify the server's node id if set
                .AddClaim("sub", state.NodeId)
                //Set ip address
                .AddClaim("ip", clientIp.ToString())
                //Add negotiaion args
                .AddClaim(FBMClient.REQ_HEAD_BUF_QUERY_ARG, CacheConfig.MaxHeaderBufferSize)
                .AddClaim(FBMClient.REQ_RECV_BUF_QUERY_ARG, CacheConfig.MaxRecvBufferSize)
                .AddClaim(FBMClient.REQ_MAX_MESS_QUERY_ARG, CacheConfig.MaxMessageSize)
                .CommitClaims();

            //Sign the auth message from our private key
            NodeConfig.KeyStore.SignJwt(auth);

            return auth;
        }

        public bool ValidateUpgrade(string? upgradeToken, string? tokenSignature, DateTimeOffset now, IPAddress connectionIp, ref string? nodeId, ref bool isPeer)
        {
            if(string.IsNullOrWhiteSpace(upgradeToken) || string.IsNullOrWhiteSpace(tokenSignature))
            {
                return false;
            }

            //Parse jwt
            using JsonWebToken jwt = JsonWebToken.Parse(upgradeToken);

            //verify signature against the cache public key, since this server must have signed it
            if (!NodeConfig.KeyStore.VerifyCachePeer(jwt))
            {
                return false;
            }

            //Recover json body
            using JsonDocument doc = jwt.GetPayload();

            //Verify audience, expiration
            if (!doc.RootElement.TryGetProperty("aud", out JsonElement audEl)
                || !string.Equals(AudienceLocalServerId, audEl.GetString(), StringComparison.OrdinalIgnoreCase))
            {
                return false;
            }

            if (!doc.RootElement.TryGetProperty("exp", out JsonElement expEl)
                || DateTimeOffset.FromUnixTimeSeconds(expEl.GetInt64()) < now)
            {
                return false;
            }

            //Check node ip address matches if required
            if (NodeConfig.VerifyIp)
            {
                if (!doc.RootElement.TryGetProperty("ip", out JsonElement ipEl))
                {
                    return false;
                }

                string? clientIp = ipEl.GetString();

                //Verify the client ip address matches the one in the token
                if (clientIp == null || !IPAddress.TryParse(clientIp, out IPAddress? clientIpAddr) || !clientIpAddr.Equals(connectionIp))
                {
                    return false;
                }
            }

            //Check if the client is a peer
            isPeer = doc.RootElement.TryGetProperty("isPeer", out JsonElement isPeerEl) && isPeerEl.GetBoolean();

            //The node id is optional and stored in the 'sub' field, ignore if the client is not a peer
            if (isPeer && doc.RootElement.TryGetProperty("sub", out JsonElement servIdEl))
            {
                nodeId = servIdEl.GetString();
            }

            //Verify token signature against a fellow cache public key
            return NodeConfig.KeyStore.VerifyUpgradeToken(tokenSignature, upgradeToken, isPeer);
        }
    }
}
