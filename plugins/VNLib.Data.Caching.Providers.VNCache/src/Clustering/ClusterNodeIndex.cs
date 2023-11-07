/*
* Copyright (c) 2023 Vaughn Nugent
* 
* Library: VNLib
* Package: VNLib.Data.Caching.Providers.VNCache
* File: ClusterNodeIndex.cs 
*
* ClusterNodeIndex.cs is part of VNLib.Data.Caching.Providers.VNCache which is part of the larger 
* VNLib collection of libraries and utilities.
*
* VNLib.Data.Caching.Providers.VNCache is free software: you can redistribute it and/or modify 
* it under the terms of the GNU Affero General Public License as 
* published by the Free Software Foundation, either version 3 of the
* License, or (at your option) any later version.
*
* VNLib.Data.Caching.Providers.VNCache is distributed in the hope that it will be useful,
* but WITHOUT ANY WARRANTY; without even the implied warranty of
* MERCHANTABILITY or FITNESS FOR A PARTICULAR PURPOSE. See the
* GNU Affero General Public License for more details.
*
* You should have received a copy of the GNU Affero General Public License
* along with this program.  If not, see https://www.gnu.org/licenses/.
*/

using System;
using System.Threading;
using System.Text.Json;
using System.Reflection;
using System.Threading.Tasks;

using VNLib.Utils.Logging;
using VNLib.Utils.Resources;
using VNLib.Data.Caching.Extensions;
using VNLib.Data.Caching.Extensions.Clustering;
using VNLib.Plugins.Extensions.Loading.Events;

namespace VNLib.Data.Caching.Providers.VNCache.Clustering
{
    internal static class ClusterNodeIndex
    {
        const string APP_DOMAIN_KEY = "vnlib.data.caching.providers.vncache";

        /*
         * Safeley determines if an instance of a node index already exists in the app domain
         * if so it returns that instance, otherwise it creates a new index instance and stores 
         * it in the app domain.
         */

        public static IClusterNodeIndex CreateIndex(CacheClientConfiguration config)
        {
            //Create a named semaphore to ensure only one index is created per app domain
            using Semaphore sm = new (1, 1, APP_DOMAIN_KEY, out _);

            if (!sm.WaitOne(500))
            {
                throw new TimeoutException("Failed to access the Cluster index shared semaphore");
            }

            try
            {
                //Try to get an existing index from the app domain
                object? remoteIndex = AppDomain.CurrentDomain.GetData(APP_DOMAIN_KEY);
                if (remoteIndex == null)
                {
                    //Create a new index and store it in the app domain
                    IClusterNodeIndex index = new LocalHandler(config);
                    AppDomain.CurrentDomain.SetData(APP_DOMAIN_KEY, index);
                    return index;
                }
                else
                {
                    //Use the existing index
                    return new RemoteHandler(remoteIndex);
                }
            }
            finally
            {
                sm.Release();
            }
        }
   

        record class LocalHandler(CacheClientConfiguration Config) : IClusterNodeIndex, IIntervalScheduleable
        {
            private Task _currentUpdate = Task.CompletedTask;

            ///<inheritdoc/>
            public CacheNodeAdvertisment? GetNextNode()
            {
                //Get all nodes
                CacheNodeAdvertisment[] ads = Config.NodeCollection.GetAllNodes();
                //Just get a random node from the collection for now
                return ads.Length > 0 ? ads.SelectRandom() : null;
            }

            ///<inheritdoc/>
            public Task WaitForDiscoveryAsync(CancellationToken cancellationToken) => _currentUpdate;

            ///<inheritdoc/>
            public Task OnIntervalAsync(ILogProvider log, CancellationToken cancellationToken)
            {
                //Run discovery operation and update the task
                _currentUpdate = Config.DiscoverNodesAsync(cancellationToken);
                return Task.CompletedTask;
            }

            /*
             * Private methods that are called via reflection
             * by remote instances of the index
             */
            internal string? SerializeNextNode()
            {
                CacheNodeAdvertisment? nextNode = GetNextNode();
                return nextNode == null ? null : JsonSerializer.Serialize(nextNode);
            }
        }

        class RemoteHandler : IClusterNodeIndex
        {
            private readonly Func<string?> _remoteSerializer;
            private readonly Func<CancellationToken, Task> _waitTask;

            public RemoteHandler(object RemoteIndex)
            {
                //get the serializer method
                _remoteSerializer = ManagedLibrary.GetMethod<Func<string?>>(RemoteIndex, nameof(LocalHandler.SerializeNextNode), BindingFlags.NonPublic);
                //get the wait task method
                _waitTask = ManagedLibrary.GetMethod<Func<CancellationToken, Task>>(RemoteIndex, nameof(WaitForDiscoveryAsync), BindingFlags.Public);
            }

            ///<inheritdoc/>
            public CacheNodeAdvertisment? GetNextNode()
            {
                //Deserialize the next node from the remote index
                string? nexNode = _remoteSerializer();
                return nexNode == null ? null : JsonSerializer.Deserialize<CacheNodeAdvertisment>(nexNode);
            }

            ///<inheritdoc/>
            public Task WaitForDiscoveryAsync(CancellationToken cancellationToken) => _waitTask(cancellationToken);

        }
    }
}