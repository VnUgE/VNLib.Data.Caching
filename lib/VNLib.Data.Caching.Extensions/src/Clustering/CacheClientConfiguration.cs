/*
* Copyright (c) 2024 Vaughn Nugent
* 
* Library: VNLib
* Package: VNLib.Data.Caching.Extensions
* File: CacheClientConfiguration.cs 
*
* CacheClientConfiguration.cs is part of VNLib.Data.Caching.Extensions which is part of the larger 
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
using System.Linq;
using System.Collections.Generic;


namespace VNLib.Data.Caching.Extensions.Clustering
{

    /// <summary>
    /// A fluent api configuration object for configuring a <see cref="FBMClient"/>
    /// to connect to cache servers.
    /// </summary>
    public class CacheClientConfiguration
    {
        /// <summary>
        /// The authentication manager to use for signing and verifying messages to and from the cache servers
        /// </summary>
        public ICacheAuthManager AuthManager { get; private set; } = null!;

        /// <summary>
        /// The error handler to use for handling errors that occur during the discovery process
        /// </summary>
        public ICacheDiscoveryErrorHandler? ErrorHandler { get; private set; }

        /// <summary>
        /// Specifies if all connections should use TLS
        /// </summary>
        public bool UseTls { get; private set; }

        internal Uri[]? WellKnownNodes { get; set; }

        /// <summary>
        /// Specifies the JWT authentication manager to use for signing and verifying JWTs
        /// </summary>
        /// <param name="manager">The authentication manager</param>
        /// <returns>Chainable fluent object</returns>
        public CacheClientConfiguration WithAuthenticator(ICacheAuthManager manager)
        {
            AuthManager = manager;
            return this;
        }

        /// <summary>
        /// Specifies if all connections should be using TLS
        /// </summary>
        /// <param name="useTls">A value that indicates if connections should use TLS</param>
        public CacheClientConfiguration WithTls(bool useTls)
        {
            UseTls = useTls;
            return this;
        }

        /// <summary>
        /// Specifies the initial cache peers to connect to
        /// </summary>
        /// <param name="peers">The collection of servers to discover peers from and connect to</param>
        /// <returns>Chainable fluent object</returns>
        /// <exception cref="ArgumentException"></exception>
        /// <exception cref="ArgumentNullException"></exception>
        public CacheClientConfiguration WithInitialPeers(IEnumerable<Uri> peers)
        {
            //Check null
            ArgumentNullException.ThrowIfNull(peers);

            //Store peer array
            WellKnownNodes = peers.ToArray();

            if (WellKnownNodes.Any(p => !p.IsAbsoluteUri))
            {
                WellKnownNodes = null;
                throw new ArgumentException("All discoverable node uris must be in absolute form");
            }

            return this;
        }

        /// <summary>
        /// Specifies the error handler to use for handling errors that occur during the discovery process
        /// </summary>
        /// <param name="handler">The error handler to use during a discovery</param>
        /// <returns>Chainable fluent object</returns>
        public CacheClientConfiguration WithErrorHandler(ICacheDiscoveryErrorHandler handler)
        {
            ErrorHandler = handler;
            return this;
        }
    }
}
