/*
* Copyright (c) 2023 Vaughn Nugent
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
    public interface ICacheListServerRequest : ICacheJwtManager
    {
        Uri DiscoveryEndpoint { get; }
    }

    /// <summary>
    /// A request container for a ListServer request
    /// </summary>
    public sealed class CacheListServerRequest : ICacheListServerRequest
    {        
        private readonly ICacheJwtManager _manager;
        

        /// <summary>
        /// The address of the broker server to connect to
        /// </summary>
        public Uri DiscoveryEndpoint { get; private set; }

        public CacheListServerRequest(ICacheJwtManager keyManager, Uri? brokerAddress = null)
        {
            _manager = keyManager;
            DiscoveryEndpoint = brokerAddress!;
        }
      
      
        /// <summary>
        /// Sets the broker address for the request
        /// </summary>
        /// <param name="brokerAddr">The broker server's address to connect to</param>
        /// <returns>A fluent chainable value</returns>
        /// <exception cref="ArgumentNullException"></exception>
        public CacheListServerRequest WithDiscoveryEndpoint(Uri brokerAddr)
        {
            DiscoveryEndpoint = brokerAddr ?? throw new ArgumentNullException(nameof(brokerAddr));
            return this;
        }

        /// <inheritdoc/>
        public void SignJwt(JsonWebToken jwt) => _manager.SignJwt(jwt);

        /// <inheritdoc/>
        public bool VerifyCache(JsonWebToken jwt) => _manager.VerifyCache(jwt);

        /// <inheritdoc/>
        public bool VerifyBroker(JsonWebToken jwt) => _manager.VerifyBroker(jwt);

        /// <inheritdoc/>
        public IReadOnlyDictionary<string, string?> GetJwtHeader() => _manager.GetJwtHeader();
    }
}
