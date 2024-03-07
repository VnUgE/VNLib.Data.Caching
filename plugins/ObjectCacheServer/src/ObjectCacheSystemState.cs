/*
* Copyright (c) 2024 Vaughn Nugent
* 
* Library: VNLib
* Package: ObjectCacheServer
* File: ObjectCacheSystemState.cs 
*
* ObjectCacheSystemState.cs is part of ObjectCacheServer which is 
* part of the larger VNLib collection of libraries and utilities.
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

using System;
using System.Linq;
using System.Net.Http;
using System.Net.Sockets;

using VNLib.Utils.Logging;
using VNLib.Utils.Memory;
using VNLib.Utils.Memory.Diagnostics;
using VNLib.Net.Messaging.FBM;
using VNLib.Plugins;
using VNLib.Plugins.Extensions.Loading;

using VNLib.Data.Caching.Extensions.Clustering;
using VNLib.Data.Caching.ObjectCache.Server.Cache;
using VNLib.Data.Caching.ObjectCache.Server.Clustering;

namespace VNLib.Data.Caching.ObjectCache.Server
{
    [ConfigurationName("cache")]
    internal sealed class ObjectCacheSystemState(PluginBase plugin, IConfigScope config) : IDisposable
    {
        const string LISTENER_LOG_SCOPE = "CacheListener";

        public BlobCacheListener<IPeerEventQueue> Listener { get; private set; } = null!;

        public ICacheStore InternalStore { get; private set; } = null!;

        /// <summary>
        /// Used for miscellaneous shared memory allocations (like the cache listener)
        /// </summary>
        public IUnmangedHeap SharedCacheHeap { get; private set; } = null!;

        /// <summary>
        /// The plugin-wide, shared node configuration
        /// </summary>
        public NodeConfig Configuration { get; } = plugin.GetOrCreateSingleton<NodeConfig>();

        /// <summary>
        /// The peer discovery manager
        /// </summary>
        public PeerDiscoveryManager PeerDiscovery { get; private set; } = null!;

        /// <summary>
        /// System wide peer monitor
        /// </summary>
        public CachePeerMonitor PeerMonitor { get; } = new();

        public CacheMemoryConfiguration MemoryConfiguration { get; } = config.Deserialze<CacheMemoryConfiguration>();

        /// <summary>
        /// The system wide peer event queue manager
        /// </summary>
        public PeerEventQueueManager PeerEventQueue { get; private set; }

        void IDisposable.Dispose()
        {
            SharedCacheHeap.Dispose();
            Listener.Dispose();
        }

        /// <summary>
        /// Initializes the cache node state
        /// </summary>
        public void Initialize()
        {
            CacheMemoryConfiguration cacheConf = MemoryConfiguration;

            ArgumentOutOfRangeException.ThrowIfLessThan(cacheConf.MaxCacheEntries, 2u);

            //Suggestion
            if (cacheConf.MaxCacheEntries < 200)
            {
                plugin.Log.Information("Suggestion: You may want a larger cache size, you have less than 200 items in cache");
            }

            LogMemConfiguration();

            //If the plugin is in debug mode enable heap tracking
            SharedCacheHeap = plugin.IsDebug() ? 
                new TrackedHeapWrapper(MemoryUtil.InitializeNewHeapForProcess(), true)
                : MemoryUtil.InitializeNewHeapForProcess();

            ConfigurePeerDiscovery();

            ConfigureCacheListener();

            PeerEventQueue = new(plugin, Configuration);
        }

        private void ConfigurePeerDiscovery()
        {
            //Get the known peers array from config, its allowed to be null for master nodes
            IConfigScope? config = plugin.TryGetConfig("known_peers");
            string[] kownPeers = config?.Deserialze<string[]>() ?? [];

            ILogProvider discLogger = plugin.Log.CreateScope(PeerDiscoveryManager.LOG_SCOPE_NAME);

            Configuration.Config.WithInitialPeers(kownPeers.Select(static s => new Uri(s)))
                .WithErrorHandler(new ErrorHandler(discLogger));

            discLogger.Information("Inital peer nodes: {nodes}", kownPeers);

            PeerDiscovery = new PeerDiscoveryManager(
                Configuration, 
                discLogger, 
                plugin.IsDebug(), 
                kownPeers.Length > 0
            );

            //Discovery manager needs to be scheduled for background work to run the discovery loop
            _ = plugin.ObserveWork(PeerDiscovery, 10);
        }

        private void ConfigureCacheListener()
        {
            /*
             * Allow loading external managed dll for a bucket-local memory manager
             */
            ICacheMemoryManagerFactory manager;

            if (string.IsNullOrWhiteSpace(MemoryConfiguration.ExternLibPath))
            {
                //Get the memory manager
                manager = plugin.GetOrCreateSingleton<BucketLocalManagerFactory>();
            }
            else
            {
                manager = plugin.CreateServiceExternal<ICacheMemoryManagerFactory>(MemoryConfiguration.ExternLibPath);
            }

            //Endpoint only allows for a single reader
            Listener = new(
                plugin.LoadMemoryCacheSystem(config, manager, MemoryConfiguration),
                plugin.GetOrCreateSingleton<CacheListenerPubQueue>(),
                plugin.Log.CreateScope(LISTENER_LOG_SCOPE),
                new SharedHeapFBMMemoryManager(SharedCacheHeap)
            );

            InternalStore = new CacheStore(Listener.Cache);
        }
      
        private void LogMemConfiguration()
        {
            const string CacheConfigTemplate =
@"
Cache Configuration:
    Max memory: {max} Mb
    Buckets: {bc}
    Entries per-bucket: {mc}
    HeapTracking: {ht}
";

            CacheMemoryConfiguration cacheConf = MemoryConfiguration;

            //calculate the max memory usage
            ulong maxByteSize = cacheConf.MaxCacheEntries * (ulong)cacheConf.BucketCount * (ulong)cacheConf.MaxMessageSize;

            //Log the cache config
            plugin.Log.Information(
                CacheConfigTemplate,
                maxByteSize / (1024 * 1000),
                cacheConf.BucketCount,
                cacheConf.MaxCacheEntries,
                plugin.IsDebug()
            );
        }

        private sealed class ErrorHandler(ILogProvider Logger) : ICacheDiscoveryErrorHandler
        {
            public void OnDiscoveryError(CacheNodeAdvertisment errorNode, Exception ex)
            {
                if (ex is HttpRequestException hre)
                {
                    if (hre.InnerException is SocketException se)
                    {
                        //transport failed
                        Logger.Warn("Failed to connect to server {serv} because {err}", errorNode, se.Message);
                    }
                    else
                    {
                        Logger.Error("Failed to connect to node {n}\n{err}", errorNode, hre);
                    }
                }
                else
                {
                    Logger.Error("Failed to discover nodes from nodeid {nid}, with error\n{err}", errorNode, ex);
                }
            }
        }
    }
}
