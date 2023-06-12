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
    public interface ICacheJwtManager
    {
        IReadOnlyDictionary<string, string?> GetJwtHeader();

        void SignJwt(JsonWebToken jwt);

        bool VerifyCache(JsonWebToken jwt);

        bool VerifyBroker(JsonWebToken jwt);
    }

    /// <summary>
    /// A fluent api configuration object for configuring a <see cref="FBMClient"/>
    /// to connect to cache servers.
    /// </summary>
    public class CacheClientConfiguration : ICacheJwtManager, ICacheListServerRequest
    {
        public ReadOnlyJsonWebKey? SigningKey { get; private set; }
        public ReadOnlyJsonWebKey? VerificationKey { get; private set; }
        public ReadOnlyJsonWebKey? BrokerVerificationKey { get; private set; }
        
        public Uri? DiscoveryEndpoint { get; private set; }
        public bool UseTls { get; private set; }
        internal ICachePeerAdvertisment[]? CacheServers { get; set; }

        /// <summary>
        /// Imports the private key used to sign messages
        /// </summary>
        /// <param name="jwk">The <see cref="ReadOnlyJsonWebKey"/> with a private key loaded</param>
        /// <returns>Chainable fluent object</returns>
        /// <exception cref="ArgumentException"></exception>
        /// <exception cref="CryptographicException"></exception>
        public CacheClientConfiguration WithSigningKey(ReadOnlyJsonWebKey jwk)
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
        public CacheClientConfiguration WithVerificationKey(ReadOnlyJsonWebKey jwk)
        {
            VerificationKey = jwk ?? throw new ArgumentNullException(nameof(jwk));
            return this;
        }

        public CacheClientConfiguration WithBrokerVerificationKey(ReadOnlyJsonWebKey jwk)
        {
            BrokerVerificationKey = jwk ?? throw new ArgumentNullException(nameof(jwk));
            return this;
        }

        /// <summary>
        /// Specifies if all connections should be using TLS
        /// </summary>
        /// <param name="useTls">A value that indicates if connections should use TLS</param>
        /// <returns>Chainable fluent object</returns>
        public CacheClientConfiguration WithTls(bool useTls)
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
        public CacheClientConfiguration WithBroker(Uri brokerAddress)
        {
            DiscoveryEndpoint = brokerAddress ?? throw new ArgumentNullException(nameof(brokerAddress));
            return this;
        }
      
        ///<inheritdoc/>
        public void SignJwt(JsonWebToken jwt) => jwt.SignFromJwk(SigningKey!);

        ///<inheritdoc/>
        public bool VerifyCache(JsonWebToken jwt) => jwt.VerifyFromJwk(VerificationKey!);

        ///<inheritdoc/>
        public bool VerifyBroker(JsonWebToken jwt) => jwt.VerifyFromJwk(BrokerVerificationKey!);

        ///<inheritdoc/>
        public IReadOnlyDictionary<string, string?> GetJwtHeader() => SigningKey!.JwtHeader;

    }
}
