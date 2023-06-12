/*
* Copyright (c) 2023 Vaughn Nugent
* 
* Library: VNLib
* Package: ObjectCacheServer
* File: INodeDiscoveryEnumerator.cs 
*
* INodeDiscoveryEnumerator.cs is part of ObjectCacheServer which is part 
* of the larger VNLib collection of libraries and utilities.
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

using System.Collections.Generic;

using VNLib.Data.Caching.Extensions;

namespace VNLib.Data.Caching.ObjectCache.Server.Distribution
{
    internal interface INodeDiscoveryEnumerator
    {
        /// <summary>
        /// Moves the enumerator to the next peer in the discovery process and returns the result
        /// </summary>
        /// <returns>The next peer advertisment in the enumeration</returns>
        ICachePeerAdvertisment? GetNextPeer();

        /// <summary>
        /// Adds the specified peer to the collection of discovered peers
        /// </summary>
        /// <param name="discoveredPeers">The peer collection</param>
        void OnPeerDiscoveryComplete(IEnumerable<ICachePeerAdvertisment> discoveredPeers);
    }
}
