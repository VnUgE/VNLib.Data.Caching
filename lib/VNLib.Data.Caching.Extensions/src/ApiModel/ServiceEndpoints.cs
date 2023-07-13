/*
* Copyright (c) 2023 Vaughn Nugent
* 
* Library: VNLib
* Package: VNLib.Data.Caching.Extensions
* File: ServiceEndpoints.cs 
*
* ServiceEndpoints.cs is part of VNLib.Data.Caching.Extensions which is part of the larger 
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
using System.Security;
using System.Text.Json;

using RestSharp;

using VNLib.Net.Http;
using VNLib.Hashing;
using VNLib.Hashing.IdentityUtility;
using VNLib.Net.Rest.Client.Construction;
using ContentType = VNLib.Net.Http.ContentType;
using VNLib.Data.Caching.Extensions.Clustering;

namespace VNLib.Data.Caching.Extensions.ApiModel
{
    /*
     * Defines the cache endpoints, builds and routes request messages to the 
     * server enpoints. In effect defines the client api for cache services.
     * 
     * This class also define methods for authentication and message verification
     */

    internal static class ServiceEndpoints
    {
        private static readonly TimeSpan MaxTimeDisparity = TimeSpan.FromSeconds(10);

        /// <summary>
        /// Get the endpoint definition for cache services for the site adapter
        /// </summary>
        internal static IRestEndpointDefinition Definition { get; } = new EndpointBuilder();

        private class EndpointBuilder : IRestEndpointDefinition
        {
            ///<inheritdoc/>
            public void BuildRequest(IRestSiteAdapter site, IRestEndpointBuilder builder)
            {
                //Define cache service endpoints/requests

                builder.WithEndpoint<DiscoveryRequest>()
                    .WithUrl(e => e.DiscoveryUrl)
                    .WithMethod(Method.Get)
                    //Accept text response (it should be a jwt)
                    .WithHeader("Accept", HttpHelpers.GetContentTypeString(ContentType.Text))
                    .WithHeader("Authorization", BuildDiscoveryAuthToken)
                    //Verify jwt responses
                    .OnResponse(VerifyJwtResponse);

                builder.WithEndpoint<NegotationRequest>()
                    .WithUrl(e => e.ConnectUrl)
                    .WithMethod(Method.Get)
                    //Accept text response (its should be a jwt)
                    .WithHeader("Accept", HttpHelpers.GetContentTypeString(ContentType.Text))
                    .WithHeader("Authorization", BuildDiscoveryAuthToken)
                    //Verify jwt responses
                    .OnResponse(VerifyJwtResponse);

                //Well known endpoint does not require authentication
                builder.WithEndpoint<GetConfigRequest>()
                    .WithUrl(gc => gc.WellKnownEp)
                    .WithMethod(Method.Get)
                    //Responses should be a signed jwt
                    .WithHeader("Accept", HttpHelpers.GetContentTypeString(ContentType.Text))
                    //Verify jwt responses
                    .OnResponse(VerifyJwtResponse);
            }
        }


        private static string BuildDiscoveryAuthToken(ICacheConnectionRequest request)
        {
            request.Challenge = RandomHash.GetRandomBase32(24);

            //Build request jwt
            using JsonWebToken jwt = new();
            jwt.WriteHeader(request.Config.AuthManager.GetJwtHeader());

            //See if the supplied config is for a cache node
            CacheNodeConfiguration? cnc = request.Config as CacheNodeConfiguration;

            //Init claim
            JwtPayload claim = jwt.InitPayloadClaim();

            claim.AddClaim("chl", request.Challenge)
                .AddClaim("iat", DateTimeOffset.UtcNow.ToUnixTimeMilliseconds());

            if (!string.IsNullOrWhiteSpace(cnc?.NodeId))
            {
                /*
                * The unique node id so the other nodes know to load the 
                * proper event queue for the current server
                */
                claim.AddClaim("sub", cnc.NodeId);
            }

            claim.CommitClaims();

            //sign the jwt
            request.Config.AuthManager.SignJwt(jwt);

            //Compile the jwt
            return jwt.Compile();
        }

        private static void VerifyJwtResponse(ICacheConnectionRequest req, RestResponse response)
        {
            byte[] data = response.RawBytes ?? throw new ArgumentException("Server response was empty, cannot continue");

            //If node config then set the is-node flag
            bool isNode = req.Config is CacheNodeConfiguration;

            //Response is jwt
            using JsonWebToken responseJwt = JsonWebToken.ParseRaw(data);

            //Verify the jwt
            if (!req.Config.AuthManager.VerifyJwt(responseJwt, isNode))
            {
                throw new SecurityException("Failed to verify the discovery server's challenge, cannot continue");
            }

            //get payload as a document
            using JsonDocument doc = responseJwt.GetPayload();

            //Verify iat times
            long iatSec = doc.RootElement.GetProperty("iat").GetInt64();

            //Get dto
            DateTimeOffset iat = DateTimeOffset.FromUnixTimeSeconds(iatSec);

            DateTimeOffset now = DateTimeOffset.UtcNow;

            //Verify iat is not before or after the current time with the disparity
            if (iat.Add(MaxTimeDisparity) < now || iat.Subtract(MaxTimeDisparity) > now)
            {
                throw new SecurityException("Server returned a request that has expired. Please check your system clock");
            }

            //If a challenge is set, verify it
            if (req.Challenge != null)
            {
                //Verify challenge
                string challenge = doc.RootElement.GetProperty("chl").GetString()
                ?? throw new SecurityException("Server did not return a challenge");

                if (!challenge.Equals(req.Challenge, StringComparison.Ordinal))
                {
                    throw new SecurityException("Server returned an invalid challenge");
                }
            }
        }

    }
}
