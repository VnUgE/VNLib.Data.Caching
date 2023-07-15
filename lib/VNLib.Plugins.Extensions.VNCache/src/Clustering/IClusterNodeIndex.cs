/*
* Copyright (c) 2023 Vaughn Nugent
* 
* Library: VNLib
* Package: VNLib.Plugins.Extensions.VNCache
* File: IClusterNodeIndex.cs 
*
* IClusterNodeIndex.cs is part of VNLib.Plugins.Extensions.VNCache which is part of the larger 
* VNLib collection of libraries and utilities.
*
* VNLib.Plugins.Extensions.VNCache is free software: you can redistribute it and/or modify 
* it under the terms of the GNU Affero General Public License as 
* published by the Free Software Foundation, either version 3 of the
* License, or (at your option) any later version.
*
* VNLib.Plugins.Extensions.VNCache is distributed in the hope that it will be useful,
* but WITHOUT ANY WARRANTY; without even the implied warranty of
* MERCHANTABILITY or FITNESS FOR A PARTICULAR PURPOSE. See the
* GNU Affero General Public License for more details.
*
* You should have received a copy of the GNU Affero General Public License
* along with this program.  If not, see https://www.gnu.org/licenses/.
*/

using System.Threading;
using System.Threading.Tasks;

using VNLib.Data.Caching.Extensions.Clustering;

namespace VNLib.Plugins.Extensions.VNCache.Clustering
{
    internal interface IClusterNodeIndex
    {
        /// <summary>
        /// Gets the next available node using the configured balancing policy
        /// or null if no nodes are available
        /// </summary>
        /// <returns>The next available node to connect to if any are available</returns>
        CacheNodeAdvertisment? GetNextNode();

        /// <summary>
        /// Waits for the discovery process to complete. This is just incase a 
        /// connection wants to happen while a long discovery is processing.
        /// </summary>
        /// <param name="cancellationToken">A token to cancel the operation</param>
        /// <returns>A task that resolves when the discovery process completes</returns>
        Task WaitForDiscoveryAsync(CancellationToken cancellationToken);
    }
}