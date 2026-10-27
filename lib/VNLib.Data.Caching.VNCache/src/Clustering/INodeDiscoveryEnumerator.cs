/*
* Copyright (c) 2026 Vaughn Nugent
* 
* Library: VNLib
* Package: VNLib.Data.Caching.VNCache
* File: INodeDiscoveryEnumerator.cs 
*
* INodeDiscoveryEnumerator.cs is part of VNLib.Data.Caching.VNCache which is part of the larger 
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


using System.Collections.Generic;

namespace VNLib.Data.Caching.VNCache.Clustering
{
    /// <summary>
    /// A custom enumerator for the node discovery process. Simplifies the recursive processes
    /// of discovering nodes in a cluster to a simple enumeration process. It allows for real-time
    /// updates to the collection of discovered nodes as a union operation.
    /// </summary>
    public interface INodeDiscoveryEnumerator : IEnumerator<CacheNodeAdvertisment>
    {
        /// <summary>
        /// Adds the specified peer to the collection of discovered peers
        /// </summary>
        /// <param name="discoveredPeers">The peer collection</param>
        void OnPeerDiscoveryComplete(IEnumerable<CacheNodeAdvertisment> discoveredPeers);
    }
}
