/*
* Copyright (c) 2023 Vaughn Nugent
* 
* Library: VNLib
* Package: VNLib.Data.Caching.Extensions
* File: ClientCacheConfiguration.cs 
*
* ClientCacheConfiguration.cs is part of VNLib.Data.Caching.Extensions which is part of the larger 
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
using System.Security.Cryptography;

using VNLib.Hashing;
using VNLib.Hashing.IdentityUtility;

namespace VNLib.Data.Caching.Extensions
{
    /// <summary>
    /// A fluent api configuration object for configuring a <see cref="FBMClient"/>
    /// to connect to cache servers.
    /// </summary>
    public sealed class ClientCacheConfiguration
    {
        internal ReadOnlyJsonWebKey? SigningKey { get; private set; }
        internal ReadOnlyJsonWebKey? VerificationKey { get; private set; }
        internal ReadOnlyJsonWebKey? BrokerVerificationKey { get; private set; }
        
        internal string ServerChallenge { get; } = RandomHash.GetRandomBase32(24);
        internal string? NodeId { get; set; }
        internal Uri? BrokerAddress { get; set; }
        internal bool UseTls { get; set; }
        internal ActiveServer[]? CacheServers { get; set; }

        internal IReadOnlyDictionary<string, string?> JwtHeader => SigningKey!.JwtHeader;

        /// <summary>
        /// Imports the private key used to sign messages
        /// </summary>
        /// <param name="jwk">The <see cref="ReadOnlyJsonWebKey"/> with a private key loaded</param>
        /// <returns>Chainable fluent object</returns>
        /// <exception cref="ArgumentException"></exception>
        /// <exception cref="CryptographicException"></exception>
        public ClientCacheConfiguration WithSigningCertificate(ReadOnlyJsonWebKey jwk)
        {
            SigningKey = jwk ?? throw new ArgumentNullException(nameof(jwk));
            return this;
        }

        /// <summary>
        /// Imports the public key used to verify messages from the remote server
        /// </summary>
        /// <param name="jwk">The <see cref="ReadOnlyJsonWebKey"/> public key only used for message verification</param>
        /// <returns>Chainable fluent object</returns>
        /// <exception cref="ArgumentException"></exception>
        /// <exception cref="CryptographicException"></exception>
        public ClientCacheConfiguration WithVerificationKey(ReadOnlyJsonWebKey jwk)
        {
            VerificationKey = jwk ?? throw new ArgumentNullException(nameof(jwk));
            return this;
        }

        public ClientCacheConfiguration WithBrokerVerificationKey(ReadOnlyJsonWebKey jwk)
        {
            BrokerVerificationKey = jwk ?? throw new ArgumentNullException(nameof(jwk));
            return this;
        }

        /// <summary>
        /// Specifies if all connections should be using TLS
        /// </summary>
        /// <param name="useTls">A value that indicates if connections should use TLS</param>
        /// <returns>Chainable fluent object</returns>
        public ClientCacheConfiguration WithTls(bool useTls)
        {
            UseTls = useTls;
            return this;   
        }
        /// <summary>
        /// Specifies the broker address to discover cache nodes from
        /// </summary>
        /// <param name="brokerAddress">The address of the server broker</param>
        /// <returns>Chainable fluent object</returns>
        /// <exception cref="ArgumentNullException"></exception>
        public ClientCacheConfiguration WithBroker(Uri brokerAddress)
        {
            this.BrokerAddress = brokerAddress ?? throw new ArgumentNullException(nameof(brokerAddress));
            return this;
        }

        /// <summary>
        /// Specifies the current server's cluster node id. If this 
        /// is a server connection attempting to listen for changes on the
        /// remote server, this id must be set and unique
        /// </summary>
        /// <param name="nodeId">The cluster node id of the current server</param>
        /// <returns>Chainable fluent object</returns>
        /// <exception cref="ArgumentNullException"></exception>
        public ClientCacheConfiguration WithNodeId(string nodeId)
        {
            this.NodeId = nodeId ?? throw new ArgumentNullException(nameof(nodeId));
            return this;
        }

        internal void SignJwt(JsonWebToken jwt)
        {
            jwt.SignFromJwk(SigningKey);
        }

        internal bool VerifyCache(JsonWebToken jwt)
        {
            return jwt.VerifyFromJwk(VerificationKey);
        }

        internal bool VerifyBroker(JsonWebToken jwt)
        {
            return jwt.VerifyFromJwk(BrokerVerificationKey);
        }
    }
}
