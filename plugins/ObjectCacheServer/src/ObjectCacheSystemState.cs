/*
* Copyright (c) 2025 Vaughn Nugent
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
using System.Threading;
using System.Threading.Tasks;
using System.Collections.Generic;

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
    /*
     * The purpose of this class is to manage the state of the entire cache server.
     * All configuration and state should be creatd and managed by this class. To make it 
     * easier to manage.
     */
    [ConfigurationName("cache")]
    internal sealed class ObjectCacheSystemState(PluginBase plugin, IConfigScope config) : IDisposable
    {
        public BlobCacheListener<IPeerEventQueue> Listener { get; private set; } = null!;

        public ICacheStore InternalStore { get; private set; } = null!;

        /// <summary>
        /// Used for miscellaneous shared memory allocations (like the cache listener)
        /// </summary>
        public IUnmanagedHeap SharedCacheHeap { get; private set; } = null!;

        /// <summary>
        /// The plugin-wide, shared node configuration
        /// </summary>
        public ServerClusterConfig ClusterConfig { get; } = plugin.GetOrCreateSingleton<ServerClusterConfig>();

        /// <summary>
        /// The system wide cache authenticator
        /// </summary>
        public CacheAuthKeyStore KeyStore { get; } = new(plugin);

        /// <summary>
        /// The system cache node configuration
        /// </summary>
        public CacheNodeConfiguration NodeConfig { get; private set; }

        /// <summary>
        /// The peer discovery manager
        /// </summary>
        public PeerDiscoveryManager PeerDiscovery { get; private set; } = null!;

        /// <summary>
        /// System wide peer monitor
        /// </summary>
        public CachePeerMonitor PeerMonitor { get; } = new();

        public CacheMemoryConfiguration MemoryConfiguration { get; } = config.Deserialize<CacheMemoryConfiguration>();

        /// <summary>
        /// The system wide peer event queue manager
        /// </summary>
        public PeerEventQueueManager PeerEventQueue { get; private set; }

        private ICacheMemoryManagerFactory _cacheMemManager;

        void IDisposable.Dispose()
        {
            SharedCacheHeap?.Dispose();
            Listener?.Dispose();
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

            PeerEventQueue = new(plugin, ClusterConfig);

            //If the plugin is in debug mode enable heap tracking
            SharedCacheHeap = plugin.IsDebug() ? 
                new TrackedHeapWrapper(MemoryUtil.InitializeNewHeapForProcess(), true)
                : MemoryUtil.InitializeNewHeapForProcess();

            //Load node configuration first
            (NodeConfig = ClusterConfig.BuildNodeConfig())
                .WithAuthenticator(KeyStore);   //Also pass the key store to the node config

            ConfigurePeerDiscovery();

            ConfigureCacheListener();
        }

        private void ConfigurePeerDiscovery()
        {
            //Get the known peers array from config, its allowed to be null for master nodes
            IConfigScope? config = plugin.Config().TryGet("known_peers");
            string[] knownPeers = config?.Deserialize<string[]>() ?? [];

            ILogProvider discLogger = plugin.Log.CreateScope(CacheConstants.LogScopes.PeerDiscovery);

            //Allow just origin nodes to be used as known peers
            IEnumerable<Uri> peerUris = knownPeers.Select(static p =>
            {
                Uri bUri = new(p, UriKind.Absolute);
                return bUri.LocalPath == "/" ? new Uri(bUri, CacheConstants.DefaultWellKnownPath) : bUri;
            });

            NodeConfig.WithInitialPeers(peerUris)
                .WithErrorHandler(new ErrorHandler(discLogger));

            discLogger.Information("Inital peer nodes: {nodes}", knownPeers);

            PeerDiscovery = new PeerDiscoveryManager(
                NodeConfig,
                ClusterConfig,
                PeerMonitor,
                discLogger, 
                plugin.IsDebug(), 
                knownPeers.Length > 0
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

            _cacheMemManager = manager;

            CacheListenerPubQueue queue = new(plugin, PeerEventQueue);

            //Must register the queue background worker to listen for changes
            _ = plugin.ObserveWork(queue, 150);

            BlobCacheListenerConfig conf = new()
            {
                Log = plugin.Log.CreateScope(CacheConstants.LogScopes.BlobCacheListener),
                MemoryManager = new SharedHeapFBMMemoryManager(SharedCacheHeap),
                EnableMessageChecksums = MemoryConfiguration.EnableChecksums,
                LogTransactions = plugin.IsDebug() || plugin.HostArgs.HasArgument("--log-cache-events")
            };

            if (conf.LogTransactions)
            {
                plugin.Log.Information("Verbose cache event logging enabled");
            }

            //Endpoint only allows for a single reader
            Listener = new(
                plugin.LoadMemoryCacheSystem(config, manager, MemoryConfiguration),
                conf,
                queue
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

        public void LogMemoryStats()
        {
            if(SharedCacheHeap is TrackedHeapWrapper thw)
            {
               const string shStatTemplate = 
@" VNCache shared heap stats:
    Current: {cur}kB
    Blocks: {blks}
    Max size: {max}kB
";              
                HeapStatistics stats = thw.GetCurrentStats();
                plugin.Log.Debug(
                    shStatTemplate, 
                    stats.AllocatedBytes / 1024, 
                    stats.AllocatedBlocks, 
                    stats.MaxHeapSize / 1024
                );

            }

            //Also print logs for the bucket local managers if they are enabled
            if(_cacheMemManager is BucketLocalManagerFactory blmf)
            {
                blmf.LogHeapStats();
            }
        }

        private sealed class ErrorHandler(ILogProvider Logger) : ICacheDiscoveryErrorHandler
        {
            public void OnDiscoveryError(CacheNodeAdvertisment errorNode, Exception ex)
                => LogError(ex, errorNode.NodeId, errorNode.ConnectEndpoint);

            public void OnDiscoveryError(Uri errorAddress, Exception ex) 
                => LogError(ex, null, errorAddress);

            private void LogError(Exception ex, string? nodId, Uri? connectAddress)
            {
                //For logging purposes, use the node id if its available, otherwise use the address
                if(nodId == null && connectAddress != null)
                {
                    nodId = connectAddress.ToString();
                }

                if (ex is HttpRequestException hre)
                {
                    if (hre.InnerException is SocketException se)
                    {
                        //transport failed
                        Logger.Warn("Failed to connect to server {serv} because {err}", nodId, se.Message);
                    }
                    else
                    {
                        Logger.Error("Failed to connect to node {n}\n{err}", nodId, hre);
                    }
                }
                if (ex is OperationCanceledException)
                {
                    Logger.Error("Failed to discover nodes from nodeid {nid}, because the operation was canceled");
                }
                else if (ex is TimeoutException)
                {
                    //Only log exception stack when in debug logging mode
                    Logger.Warn("Failed to discover nodes from nodeid {nid}, because a timeout occured", nodId);
                }
                else
                {
                    //Only log exception stack when in debug logging mode
                    if (Logger.IsEnabled(LogLevel.Debug))
                    {
                        Logger.Error("Failed to discover nodes from nodeid {nid}, with error\n{err}", nodId, ex);
                    }
                    else
                    {
                        Logger.Error("Failed to discover nodes from nodeid {nid}, with error: {err}", nodId, ex.Message);
                    }
                }
            }
        }

        internal sealed class CacheStore(IBlobCacheTable table) : ICacheStore
        {

            ///<inheritdoc/>
            ValueTask ICacheStore.AddOrUpdateBlobAsync<T>(string objectId, string? alternateId, ObjectDataGet<T> bodyData, T state, CancellationToken token)
            {
                return table.AddOrUpdateObjectAsync(objectId, alternateId, bodyData, state, default, token);
            }

            ///<inheritdoc/>
            void ICacheStore.Clear()
            {
                throw new NotImplementedException();
            }

            ///<inheritdoc/>
            ValueTask<bool> ICacheStore.DeleteItemAsync(string id, CancellationToken token)
            {
                return table.DeleteObjectAsync(id, token);
            }
        }
    }
}
