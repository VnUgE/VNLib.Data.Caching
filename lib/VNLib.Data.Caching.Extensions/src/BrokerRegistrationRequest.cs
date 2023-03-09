/*
* Copyright (c) 2023 Vaughn Nugent
* 
* Library: VNLib
* Package: VNLib.Data.Caching.Extensions
* File: BrokerRegistrationRequest.cs 
*
* BrokerRegistrationRequest.cs is part of VNLib.Data.Caching.Extensions which is part of the larger 
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
using System.Collections.Generic;

using VNLib.Utils;
using VNLib.Hashing.IdentityUtility;


namespace VNLib.Data.Caching.Extensions
{
    /// <summary>
    /// A broker registration request message in a fluent api
    /// format. This message may be disposed when no longer in use
    /// </summary>
    public sealed class BrokerRegistrationRequest : VnDisposeable
    {
        private bool ownsKey;
        private ReadOnlyJsonWebKey? SigningKey;

        /// <summary>
        /// The cache server node id
        /// </summary>
        public string? NodeId { get; private set; }
        /// <summary>
        /// The broker server's address
        /// </summary>
        public Uri? BrokerAddress { get; private set; }
        /// <summary>
        /// The security token used by the broker server to 
        /// authenticate during heartbeat connections
        /// </summary>
        public string? HeartbeatToken { get; private set; }
        /// <summary>
        /// The address for remote clients to use to 
        /// connect to this server
        /// </summary>
        public string? RegistrationAddress { get; private set; }      

        /// <summary>
        /// Recovers the private key from the supplied certificate
        /// </summary>
        /// <param name="jwk">The private key used to sign messages</param>
        /// <param name="ownsKey">A value that indicates if the current instance owns the key</param>
        /// <returns></returns>
        /// <exception cref="ArgumentException"></exception>
        public BrokerRegistrationRequest WithSigningKey(ReadOnlyJsonWebKey jwk, bool ownsKey)
        {
            this.ownsKey = ownsKey;
            SigningKey = jwk ?? throw new ArgumentNullException(nameof(jwk));
            return this;
        }

        public BrokerRegistrationRequest WithBroker(Uri brokerUri)
        {
            BrokerAddress = brokerUri;
            return this;
        }

        public BrokerRegistrationRequest WithRegistrationAddress(string address)
        {
            RegistrationAddress = address;
            return this;
        }

        public BrokerRegistrationRequest WithHeartbeatToken(string token)
        {
            HeartbeatToken = token;
            return this;
        }
        
        public BrokerRegistrationRequest WithNodeId(string nodeId)
        {
            NodeId = nodeId;
            return this;
        }

        internal void SignJwt(JsonWebToken jwt)
        {
            jwt.SignFromJwk(SigningKey);
        }

        internal IReadOnlyDictionary<string, string?> JsonHeader => SigningKey!.JwtHeader;

        ///<inheritdoc/>
        protected override void Free()
        {
            if (ownsKey)
            {
                SigningKey?.Dispose();
            }
        }
    }
}
