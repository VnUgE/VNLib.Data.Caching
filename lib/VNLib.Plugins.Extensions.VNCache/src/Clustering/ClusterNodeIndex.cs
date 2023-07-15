/*
* Copyright (c) 2023 Vaughn Nugent
* 
* Library: VNLib
* Package: VNLib.Plugins.Extensions.VNCache
* File: ClusterNodeIndex.cs 
*
* ClusterNodeIndex.cs is part of VNLib.Plugins.Extensions.VNCache which is part of the larger 
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

using VNLib.Utils.Logging;
using VNLib.Data.Caching.Extensions;
using VNLib.Data.Caching.Extensions.Clustering;
using VNLib.Plugins.Extensions.Loading.Events;

namespace VNLib.Plugins.Extensions.VNCache.Clustering
{
    internal sealed class ClusterNodeIndex : IClusterNodeIndex, IIntervalScheduleable
    {
        private readonly CacheClientConfiguration _config;
        private Task _currentUpdate;


        public ClusterNodeIndex(CacheClientConfiguration config)
        {
            _config = config;
            _currentUpdate = Task.CompletedTask;
        }

        ///<inheritdoc/>
        public CacheNodeAdvertisment? GetNextNode()
        {
            //Get all nodes
            CacheNodeAdvertisment[] ads = _config.NodeCollection.GetAllNodes();
            //Just get a random node from the collection for now
            return ads.Length > 0 ? ads.SelectRandom() : null;
        }

        ///<inheritdoc/>
        public Task WaitForDiscoveryAsync(CancellationToken cancellationToken)
        {
            return _currentUpdate.WaitAsync(cancellationToken);
        }

        /// <summary>
        /// Runs the discovery process and updates the current update task
        /// </summary>
        /// <param name="log"></param>
        /// <param name="cancellationToken">A token to cancel the operation</param>
        /// <returns>A task that completes when the discovery process is complete</returns>
        public Task OnIntervalAsync(ILogProvider log, CancellationToken cancellationToken)
        {
            //Run discovery operation and update the task
            _currentUpdate = _config.DiscoverNodesAsync(cancellationToken);
            return Task.CompletedTask;
        }
    }
}