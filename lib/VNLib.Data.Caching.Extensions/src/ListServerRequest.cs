/*
* Copyright (c) 2022 Vaughn Nugent
* 
* Library: VNLib
* Package: VNLib.Data.Caching.Extensions
* File: ListServerRequest.cs 
*
* ListServerRequest.cs is part of VNLib.Data.Caching.Extensions which is part of the larger 
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
    /// A request container for a ListServer request
    /// </summary>
    public sealed class ListServerRequest : VnDisposeable
    {
        private readonly bool _ownsKeys;

        private ReadOnlyJsonWebKey? VerificationKey;
        private ReadOnlyJsonWebKey? SigningAlg;

        /// <summary>
        /// The address of the broker server to connect to
        /// </summary>
        public Uri BrokerAddress { get; }

        public ListServerRequest(Uri brokerAddress)
        {
            BrokerAddress = brokerAddress;
            _ownsKeys = true;
        }
        
        private ListServerRequest(ClientCacheConfiguration conf)
        {
            //Broker verification key is required
            VerificationKey = conf.BrokerVerificationKey;
            SigningAlg = conf.SigningKey;
            BrokerAddress = conf.BrokerAddress ?? throw new ArgumentException("Broker address must be specified");
            _ownsKeys = false;
        }

        internal static ListServerRequest FromConfig(ClientCacheConfiguration conf) => new (conf);

        /// <summary>
        /// Sets the public key used to verify the signature of the response.
        /// </summary>
        /// <param name="jwk">The key used to verify messages </param>
        public ListServerRequest WithVerificationKey(ReadOnlyJsonWebKey jwk)
        {            
            VerificationKey = jwk ?? throw new ArgumentNullException(nameof(jwk));
            return this;
        }
        /// <summary>
        /// Sets the private key used to sign the request.
        /// </summary>
        /// <param name="jwk">The <see cref="ReadOnlyJsonWebKey"/> containing the private key used to sign the message</param>
        /// <exception cref="ArgumentNullException"></exception>
        public ListServerRequest WithSigningKey(ReadOnlyJsonWebKey jwk)
        {
            SigningAlg = jwk ?? throw new ArgumentNullException(nameof(jwk));
            return this;
        }

        /// <summary>
        /// Signs the <see cref="JsonWebToken"/> using the private key.
        /// </summary>
        /// <param name="jwt">The message to sign</param>
        internal void SignJwt(JsonWebToken jwt)
        {
            jwt.SignFromJwk(SigningAlg);
        }

        /// <summary>
        /// Verifies the signature of the <see cref="JsonWebToken"/>
        /// </summary>
        /// <param name="jwt"></param>
        /// <returns>A value that indicates if the signature is verified</returns>
        internal bool VerifyJwt(JsonWebToken jwt)
        {
            return jwt.VerifyFromJwk(VerificationKey);
        }

        internal IReadOnlyDictionary<string, string?> JwtHeader => SigningAlg!.JwtHeader;

        ///<inheritdoc/>
        protected override void Free()
        {
            if (_ownsKeys)
            {
                VerificationKey?.Dispose();
                SigningAlg?.Dispose();
            }
        }
    }
}
