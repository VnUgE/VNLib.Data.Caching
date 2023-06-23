/*
* Copyright (c) 2023 Vaughn Nugent
* 
* Library: VNLib
* Package: VNLib.Data.Caching.Extensions
* File: INodeDiscoveryCollection.cs 
*
* INodeDiscoveryCollection.cs is part of VNLib.Data.Caching.Extensions which is part of the larger 
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


using System.Collections.Generic;

namespace VNLib.Data.Caching.Extensions
{
    /// <summary>
    /// Represents a collection of discovered nodes
    /// </summary>
#pragma warning disable CA1711 // Identifiers should not have incorrect suffix
    public interface INodeDiscoveryCollection
#pragma warning restore CA1711 // Identifiers should not have incorrect suffix
    {
        /// <summary>
        /// Begins a new discovery and gets an enumerator for the discovery process
        /// </summary>
        /// <returns>An enumerator that simplifies discovery of unique nodes</returns>
        INodeDiscoveryEnumerator BeginDiscovery();

        /// <summary>
        /// Begins a new discovery and gets an enumerator for the discovery process
        /// </summary>
        /// <param name="initialPeers">An initial collection of peers to add to the enumeration</param>
        /// <returns>An enumerator that simplifies discovery of unique nodes</returns>
        INodeDiscoveryEnumerator BeginDiscovery(IEnumerable<ICacheNodeAdvertisment> initialPeers);

        /// <summary>
        /// Gets a snapshot of all discovered nodes in the current collection.
        /// </summary>
        /// <returns>The current collection of notes</returns>
        ICacheNodeAdvertisment[] GetAllNodes();

        /// <summary>
        /// Completes a discovery process and updates the collection with the results
        /// </summary>
        /// <param name="enumerator">The enumerator used to collect discovered nodes</param>
        void CompleteDiscovery(INodeDiscoveryEnumerator enumerator);
    }
}
