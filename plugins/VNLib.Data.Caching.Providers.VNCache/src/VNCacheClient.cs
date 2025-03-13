﻿/*
* Copyright (c) 2025 Vaughn Nugent
* 
* Library: VNLib
* Package: VNLib.Data.Caching.Providers.VNCache
* File: VNCacheClient.cs 
*
* VNCacheClient.cs is part of VNLib.Data.Caching.Providers.VNCache which is 
* part of the larger VNLib collection of libraries and utilities.
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
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using System.Diagnostics;

using VNLib.Utils.Logging;
using VNLib.Plugins;
using VNLib.Plugins.Extensions.Loading;

using VNLib.Data.Caching.Providers.VNCache.Internal;
/*
 * This package exports an IGlobalCacheProvider that is intended to be packaged by 
 * application distributors that want to use VNCache as a global cache for their 
 * application.
 * 
 * This package allows for memory only caching, write-through memory cache, and 
 * direct remote caching using VNCache as the backend.
 */

namespace VNLib.Data.Caching.Providers.VNCache
{

    /// <summary>
    /// The VNCache global cache provider client, that is intended to be loaded
    /// using <see cref="LoadingExtensions.GetOrCreateSingleton{T}(PluginBase)"/> directly
    /// on the plugin loading a cache client.
    /// <para>
    /// Users may also create cache instances outside of plugin context using static 
    /// methods.
    /// </para>
    /// </summary>
    [ServiceExport]
    [ConfigurationName(CACHE_CONFIG_KEY)]
    public sealed class VNCacheClient : IGlobalCacheProvider, IAsyncBackgroundWork
    {
        internal const string CACHE_CONFIG_KEY = "cache";
        internal const string MEMORY_CACHE_CONFIG_KEY = "memory_cache";
        internal const string MEMORY_CACHE_ONLY_KEY = "memory_only";         

        /// <summary>
        /// Allows you to programatically create a remote-only VNCache instance
        /// </summary>
        /// <param name="config">The remote cache configuration, required for VNCache remote cache servers</param>
        /// <param name="debugLog">An optional FBMClient debugging log provider, should be null unless debug logging is desired </param>
        /// <returns>An opreator handle that can schedule the remote cache worker task</returns>
        /// <exception cref="ArgumentNullException"></exception>
        /// <remarks>
        /// The returned <see cref="CacheClientHandle"/> implements the <see cref="IAsyncBackgroundWork"/>
        /// interface and must be scheduled in order to maintain a connection with the remote cache store.
        /// </remarks>
        public static CacheClientHandle CreateRemoteCache(VnCacheClientConfig config)
        {
            ArgumentNullException.ThrowIfNull(config);           
            return new(new FBMCacheClient(config));
        }

        /// <summary>
        /// Allows you to programatically create your own instance if a VNCache remote server backed
        /// memory cache programatically. 
        /// </summary>
        /// <param name="remote">The remote cache configuration, required for VNCache remote cache servers</param>
        /// <param name="memory">The local memory backed configuration</param>
        /// <param name="debugLog">An optional FBMClient debugging log provider, should be null unless debug logging is desired </param>
        /// <returns>An opreator handle that can schedule the remote cache worker task</returns>
        /// <exception cref="ArgumentNullException"></exception>
        /// <remarks>
        /// The returned <see cref="CacheClientHandle"/> implements the <see cref="IAsyncBackgroundWork"/>
        /// interface and must be scheduled in order to maintain a connection with the remote cache store. The memory cache 
        /// resources are released when the worker task exits.
        /// </remarks>
        public static CacheClientHandle CreateRemoteBackedMemoryCache(VnCacheClientConfig remote, MemoryCacheConfig memory)
        {
            ArgumentNullException.ThrowIfNull(remote);
            ArgumentNullException.ThrowIfNull(memory);
            return CreateRemoteBackedMemoryCache(remote, memory, plugin: null);
        }

        /// <summary>
        /// Allows you to programatically create a memory only <see cref="IGlobalCacheProvider"/>
        /// cache instance.
        /// </summary>
        /// <param name="config">The memory cache configuration</param>
        /// <returns>
        /// A <see cref="MemoryCacheOperator"/> handle that holds a ready-to use cache instance. 
        /// This operator must be disposed to release held resources.
        /// </returns>
        /// <exception cref="ArgumentNullException"></exception>
        public static CacheClientHandle CreateMemoryCache(MemoryCacheConfig config)
        {
            ArgumentNullException.ThrowIfNull(config);
            return CreateMemoryCache(config, plugin: null);
        }

        /*
         * This private functions serve to streaming the call surface area 
         * so as many functions as possible are tested in the public API
         * and for consitency. Whatever we expect the public to use, we should
         * also use internally.
         */

        private static CacheClientHandle CreateMemoryCache(MemoryCacheConfig config, PluginBase? plugin)
        {
            Debug.Assert(config != null);
          
            config.OnValidate();

            return new CacheClientHandle(cache: new MemoryCache(config, plugin));
        }
       
        private static CacheClientHandle CreateRemoteCache(VnCacheClientConfig config, PluginBase? plugin)
        {
            Debug.Assert(config != null);

            config.OnValidate();

            //Create a new client that depends on the plugin context
            FBMCacheClient client = plugin is not null 
                ? new(plugin, config) 
                : new(config);

            return new(client);
        }

        private static CacheClientHandle CreateRemoteBackedMemoryCache(VnCacheClientConfig remote, MemoryCacheConfig memory, PluginBase? plugin)
        {
            Debug.Assert(remote != null);
            Debug.Assert(memory != null);
            
            remote.OnValidate();
            memory.OnValidate();

            //Create a new client that depends on the plugin context
            FBMCacheClient fbmCache = plugin is not null
                ? new(plugin, remote)
                : new(remote);

            RemoteBackedMemoryCache cache = new(memory, fbmCache);
            return new (cache);
        }


        private readonly IGlobalCacheProvider _client;
        private readonly CacheClientHandle _handle;

        public VNCacheClient(PluginBase plugin, IConfigScope config)
        {
            ArgumentNullException.ThrowIfNull(plugin);
            ArgumentNullException.ThrowIfNull(config);

            //Read the cache client configuration object
            VnCacheClientConfig cacheClientConfig = config.Deserialze<VnCacheClientConfig>()!;

            //Always assign a debug log in plugin context
            cacheClientConfig.ClientDebugLog = plugin.Log.CreateScope("CLIENT");

            if (config.TryGetValue(MEMORY_CACHE_CONFIG_KEY, out JsonElement memCacheConfEl))
            {
                MemoryCacheConfig memoryConfig = memCacheConfEl.Deserialize<MemoryCacheConfig>()!;

                //Use the plugin configuration to create the memory manager
                memoryConfig.MemoryManagerFactory = plugin.GetOrCreateSingleton<BucketLocalManagerFactory>();

                if (config.TryGetValue(MEMORY_CACHE_ONLY_KEY, out JsonElement memOnly) && memOnly.GetBoolean())
                {
                    _handle = CreateMemoryCache(memoryConfig, plugin);
                }
                else
                {
                    _handle = CreateRemoteBackedMemoryCache(
                        remote: cacheClientConfig,
                        memory: memoryConfig,
                        plugin
                    );
                }
            }
            else
            {
                _handle = CreateRemoteCache(cacheClientConfig, plugin);
            }

            _client = _handle.Cache;
        }


        ///<inheritdoc/>
        Task IAsyncBackgroundWork.DoWorkAsync(ILogProvider pluginLog, CancellationToken exitToken)
        {
            /*
             * When running in plugin context, this function will be called. It will
             * invoke the run function for the backing cache store (using the operator 
             * handle)
             */
            return _handle.RunAsync(
                pluginLog.CreateScope("VNCache"), 
                exitToken
            );
        }

        ///<inheritdoc/>
        public bool IsConnected => _client.IsConnected;

        ///<inheritdoc/>
        public ICacheObjectDeserializer DefaultDeserializer => _client.DefaultDeserializer;

        ///<inheritdoc/>
        public ICacheObjectSerializer DefaultSerializer => _client.DefaultSerializer;

        ///<inheritdoc/>
        public Task AddOrUpdateAsync<T>(string key, string? newKey, T value, ICacheObjectSerializer serialzer, CancellationToken cancellation)
        {
            return _client.AddOrUpdateAsync(key, newKey, value, serialzer, cancellation);
        }

        ///<inheritdoc/>
        public Task AddOrUpdateAsync<T>(string key, string? newKey, ObjectDataGet<T> callback, T state, CancellationToken cancellation)
        {
            return _client.AddOrUpdateAsync(key, newKey, callback, state, cancellation);
        }

        ///<inheritdoc/>
        public Task<bool> DeleteAsync(string key, CancellationToken cancellation)
        {
            return _client.DeleteAsync(key, cancellation);
        }

        ///<inheritdoc/>
        public Task<T?> GetAsync<T>(string key, ICacheObjectDeserializer deserializer, CancellationToken cancellation)
        {
            return _client.GetAsync<T>(key, deserializer, cancellation);
        }

        ///<inheritdoc/>
        public Task GetAsync<T>(string key, ObjectDataSet<T> callback, T state, CancellationToken cancellation)
        {
            return _client.GetAsync(key, callback, state, cancellation);
        }

        ///<inheritdoc/>
        public object GetUnderlyingStore()
        {
            return _client.GetUnderlyingStore();
        }       
    }
}
