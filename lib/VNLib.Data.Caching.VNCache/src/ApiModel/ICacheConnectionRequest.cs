/*
* Copyright (c) 2026 Vaughn Nugent
* 
* Library: VNLib
* Package: VNLib.Data.Caching.VNCache
* File: ICacheConnectionRequest.cs 
*
* ICacheConnectionRequest.cs is part of VNLib.Data.Caching.VNCache which is part of the larger 
* VNLib collection of libraries and utilities.
*
* VNLib.Data.Caching.VNCache is free software: you can redistribute it and/or modify 
* it under the terms of the GNU Affero General Public License as 
* published by the Free Software Foundation, either version 3 of the
* License, or (at your option) any later version.
*
* VNLib.Data.Caching.VNCache is distributed in the hope that it will be useful,
* but WITHOUT ANY WARRANTY; without even the implied warranty of
* MERCHANTABILITY or FITNESS FOR A PARTICULAR PURPOSE. See the
* GNU Affero General Public License for more details.
*
* You should have received a copy of the GNU Affero General Public License
* along with this program.  If not, see https://www.gnu.org/licenses/.
*/

using VNLib.Data.Caching.VNCache.Clustering;

namespace VNLib.Data.Caching.VNCache.ApiModel
{
    /// <summary>
    /// Represents a request to connect to a cache server.
    /// </summary>
    internal interface ICacheConnectionRequest
    {
        /// <summary>
        /// The <see cref="CacheClientConfiguration"/> used to configure, authenticate, and 
        /// verify messages sent to and received from cache servers.
        /// </summary>
        CacheClientConfiguration Config { get; }

        /// <summary>
        /// An optional challenge string to be used during the authentication 
        /// process. When set, is sent in the request JWT, and is expected to 
        /// be returned in the response JWT.
        /// </summary>
        string? Challenge { get; set; }
    }
}
