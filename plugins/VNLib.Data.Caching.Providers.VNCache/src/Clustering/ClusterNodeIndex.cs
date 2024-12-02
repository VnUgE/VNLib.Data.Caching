/*
* Copyright (c) 2024 Vaughn Nugent
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

        public static IClusterNodeIndex CreateIndex(VNCacheClusterManager cluster)
        {
            /* TEMPORARY: 
             * Named semaphores are only supported on Windows, which allowed synchronized communication between 
             * plugins, but this is not supported on Linux. This will be replaced with a more robust solution
             * in the future. For now they will just need to be separate instances.
             * 
             * Remember while plugins are in the same app-domain, they do not share an assembly 
             * load context which means unless the default ALC contains the desired types, types won't unify
             * so we have to use "ghetto" features to avoid interprocess communication, in the same process...
             */

            if (OperatingSystem.IsWindows())
            {
                //Create a named semaphore to ensure only one index is created per app domain
                using Semaphore sm = new (1, 1, APP_DOMAIN_KEY, out _);

                if (!sm.WaitOne(500))
                {
                    throw new TimeoutException("Failed to access the Cluster index shared semaphore");
                }

                try
                {
                    //Try to get an existing index from the app domain global storage pool
                    object? remoteIndex = AppDomain.CurrentDomain.GetData(APP_DOMAIN_KEY);
                    if (remoteIndex == null)
                    {
                        //Create a new index and store it in the app domain
                        IClusterNodeIndex index = new LocalHandler(cluster);
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
            else
            {
                return new LocalHandler(cluster);
            }
        }
   
        /*
         * So a bit of explaination. 
         * 
         * Plugins don't share types. Each plugin will load this package into its own ALC. Which will
         * cause n instances of the cluster indext manager. Which can cause unecessary http traffic 
         * building the cluster index multiple times. In an attemt to avoid this, I try to share a single
         * cluster index instance across all plugins in the same app domain. 
         * 
         * To do this a local handler instance is loaded into whichever plugin accuires the named semaphore
         * first, and then the instance is stored in the app domain global storage pool. If its found,
         * then other plugins will use the remote handler to access the index.
         * 
         * The remote handler, attempts to use reflection to get function delegates and call the local 
         * handler functions via reflection. 
         * 
         * Unless VNLib.Core supports a new way to safley share types across ALCs, this is my solution.
         */

        sealed class LocalHandler(VNCacheClusterManager cluster) : IClusterNodeIndex, IIntervalScheduleable
        {
            private Task _currentUpdate = Task.CompletedTask;

            ///<inheritdoc/>
            public CacheNodeAdvertisment? GetNextNode()
            {
                //Get all discovered nodes
                CacheNodeAdvertisment[] ads = cluster.DiscoveredNodes.GetAllNodes();
                //Just get a random node from the collection for now
                return ads.Length > 0 
                    ? ads.SelectRandom() 
                    : null;
            }

            ///<inheritdoc/>
            public Task WaitForDiscoveryAsync(CancellationToken cancellationToken) => _currentUpdate;

            ///<inheritdoc/>
            public Task OnIntervalAsync(ILogProvider log, CancellationToken cancellationToken)
            {
                //Run discovery operation and update the task
                _currentUpdate = cluster.DiscoverNodesAsync(cancellationToken);
                return Task.CompletedTask;
            }

            /*
             * Private methods that are called via reflection
             * by remote instances of the index
             */
            internal string? SerializeNextNode()
            {
                CacheNodeAdvertisment? nextNode = GetNextNode();
                return nextNode is not null 
                    ? JsonSerializer.Serialize(nextNode) 
                    : null;
            }
        }

        sealed class RemoteHandler(object RemoteIndex) : IClusterNodeIndex
        {
            private readonly Func<string?> _remoteSerializer 
                = ManagedLibrary.GetMethod<Func<string?>>(RemoteIndex, nameof(LocalHandler.SerializeNextNode), BindingFlags.NonPublic);

            private readonly Func<CancellationToken, Task> _waitTask 
                = ManagedLibrary.GetMethod<Func<CancellationToken, Task>>(RemoteIndex, nameof(LocalHandler.WaitForDiscoveryAsync), BindingFlags.Public);

            ///<inheritdoc/>
            public CacheNodeAdvertisment? GetNextNode()
            {
                //Deserialize the next node from the remote index
                string? nexNode = _remoteSerializer();
                return nexNode is not null
                    ? JsonSerializer.Deserialize<CacheNodeAdvertisment>(nexNode) 
                    : null;
            }

            ///<inheritdoc/>
            public Task WaitForDiscoveryAsync(CancellationToken cancellationToken) => _waitTask(cancellationToken);

        }
    }
}