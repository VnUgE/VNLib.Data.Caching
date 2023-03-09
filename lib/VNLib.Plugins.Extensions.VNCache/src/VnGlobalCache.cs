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

using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

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
    }
}