/*
* Copyright (c) 2023 Vaughn Nugent
* 
* Library: VNLib
* Package: VNLib.Plugins.Extensions.VNCache
* File: VnGlobalCache.cs 
*
* VnGlobalCache.cs is part of VNLib.Plugins.Extensions.VNCache which is part of the larger 
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

using System;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

using VNLib.Utils.Logging;
using VNLib.Data.Caching;
using VNLib.Plugins.Extensions.Loading;

namespace VNLib.Plugins.Extensions.VNCache
{

    /// <summary>
    /// A wrapper to simplify a shared global cache client
    /// </summary>
    [ConfigurationName(VNCacheExtensions.CACHE_CONFIG_KEY)]
    public sealed class VnGlobalCache : IGlobalCacheProvider
    {
        private readonly IGlobalCacheProvider _client;

        /// <summary>
        /// Initializes an emtpy client wrapper that still requires 
        /// configuration loading
        /// </summary>
        public VnGlobalCache(PluginBase pbase, IConfigScope config)
        {
            if (config.TryGetValue(VNCacheExtensions.MEMORY_CACHE_CONFIG_KEY, out _))
            {
                //Check for memory only flag
                if (config.TryGetValue(VNCacheExtensions.MEMORY_CACHE_ONLY_KEY, out JsonElement memOnly) && memOnly.GetBoolean())
                {
                    //Create a memory-only cache
                    _client = pbase.GetOrCreateSingleton<MemoryCache>();
                }
                else
                {
                    //Remote-backed memory cache
                    _client = pbase.GetOrCreateSingleton<RemoteBackedMemoryCache>();
                }
            }
            else
            {
                //Setup non-memory backed cache client
                _client = pbase.GetOrCreateSingleton<VnCacheClient>();
            }
        }


        /// <summary>
        /// Allows you to programatically create a remote-only VNCache instance
        /// </summary>
        /// <param name="config">The remote cache configuration, required for VNCache remote cache servers</param>
        /// <param name="debugLog">An optional FBMClient debugging log provider, should be null unless debug logging is desired </param>
        /// <returns>An opreator handle that can schedule the remote cache worker task</returns>
        /// <exception cref="ArgumentNullException"></exception>
        /// <remarks>
        /// The returned <see cref="RemoteCacheOperator"/> implements the <see cref="IAsyncBackgroundWork"/>
        /// interface and must be scheduled in order to maintain a connection with the remote cache store.
        /// </remarks>
        public static RemoteCacheOperator CreateRemoteCache(VnCacheClientConfig config, ILogProvider? debugLog = null)
        {
            _ = config ?? throw new ArgumentNullException(nameof(config));

            //Init client
            VnCacheClient client = new(config, debugLog);

            //Return single handle
            return new(client);
        }

        /// <summary>
        /// Allows you to programtically create your own instance if a VNCache remote server backed
        /// memory cache programatically. 
        /// </summary>
        /// <param name="remote">The remote cache configuration, required for VNCache remote cache servers</param>
        /// <param name="memory">The local memory backed configuration</param>
        /// <param name="debugLog">An optional FBMClient debugging log provider, should be null unless debug logging is desired </param>
        /// <returns>An opreator handle that can schedule the remote cache worker task</returns>
        /// <exception cref="ArgumentNullException"></exception>
        /// <remarks>
        /// The returned <see cref="RemoteCacheOperator"/> implements the <see cref="IAsyncBackgroundWork"/>
        /// interface and must be scheduled in order to maintain a connection with the remote cache store. The memory cache 
        /// resources are released when the worker task exits.
        /// </remarks>
        public static RemoteCacheOperator CreateRemoteBackedMemoryCache(VnCacheClientConfig remote, MemoryCacheConfig memory, ILogProvider? debugLog)
        {
            _ = remote ?? throw new ArgumentNullException(nameof(remote));
            _ = memory ?? throw new ArgumentNullException(nameof(memory));

            //Init client
            RemoteBackedMemoryCache client = new(remote, memory, debugLog);

            //Return single handle
            return new(client);
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
        public static MemoryCacheOperator CreateMemoryCache(MemoryCacheConfig config)
        {
            _ = config ?? throw new ArgumentNullException(nameof(config));

            //Init client
            MemoryCache cache = new(config);

            //Return single handle
            return new(cache);
        }
    

        ///<inheritdoc/>
        public bool IsConnected => _client.IsConnected;

        ///<inheritdoc/>
        public Task AddOrUpdateAsync<T>(string key, string? newKey, T value, CancellationToken cancellation)
        {
            return _client.AddOrUpdateAsync(key, newKey, value, cancellation);
        }

        ///<inheritdoc/>
        public Task AddOrUpdateAsync<T>(string key, string? newKey, T value, ICacheObjectSerialzer serialzer, CancellationToken cancellation)
        {
            return _client.AddOrUpdateAsync(key, newKey, value, serialzer, cancellation);
        }

        ///<inheritdoc/>
        public Task DeleteAsync(string key, CancellationToken cancellation)
        {
            return _client.DeleteAsync(key, cancellation);
        }

        ///<inheritdoc/>
        public Task<T?> GetAsync<T>(string key, CancellationToken cancellation)
        {
            return _client.GetAsync<T>(key, cancellation);
        }

        ///<inheritdoc/>
        public Task<T?> GetAsync<T>(string key, ICacheObjectDeserialzer deserializer, CancellationToken cancellation)
        {
            return _client.GetAsync<T>(key, deserializer, cancellation);
        }

        ///<inheritdoc/>
        public Task GetAsync(string key, IObjectData rawData, CancellationToken cancellation)
        {
            return _client.GetAsync(key, rawData, cancellation);
        }

        ///<inheritdoc/>
        public Task AddOrUpdateAsync(string key, string? newKey, IObjectData rawData, ICacheObjectSerialzer serialzer, CancellationToken cancellation)
        {
            return _client.AddOrUpdateAsync(key, newKey, rawData, serialzer, cancellation);
        }
    }
}