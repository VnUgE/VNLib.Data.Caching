﻿/*
* Copyright (c) 2023 Vaughn Nugent
* 
* Library: VNLib
* Package: VNLib.Plugins.Extensions.VNCache
* File: RemoteBackedMemoryCache.cs 
*
* RemoteBackedMemoryCache.cs is part of VNLib.Plugins.Extensions.VNCache 
* which is part of the larger VNLib collection of libraries and utilities.
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

using VNLib.Utils;
using VNLib.Utils.Memory;
using VNLib.Utils.Logging;
using VNLib.Utils.Memory.Diagnostics;
using VNLib.Data.Caching;
using VNLib.Data.Caching.ObjectCache;
using VNLib.Plugins.Extensions.Loading;

namespace VNLib.Plugins.Extensions.VNCache
{
    [ConfigurationName(VNCacheExtensions.CACHE_CONFIG_KEY)]
    internal sealed class MemoryCache : VnDisposeable, IGlobalCacheProvider
    {
        const int MB_DIVISOR = 1000 * 1024;

        const string DEBUG_TEMPLATE =@"Configuring Memory-Only Cache
 | -----------------------------
 | Configuration:
 |   Table Size:  {ts}
 |   Bucket Size: {bs}
 |   Max Objects: {obj}
 | Memory Estimations:
 |   4K blocks: {4k}Mb
 |   8K blocks: {8k}Mb
 |  16K blocks: {16K}Mb
 | -----------------------------
";

        private readonly ICacheObjectSerialzer _serialzer;
        private readonly ICacheObjectDeserialzer _deserialzer;
        private readonly IBlobCacheTable _memCache;
        private readonly IUnmangedHeap _bufferHeap;

        public MemoryCache(PluginBase pbase, IConfigScope config)
        {
            //Get nested memory cache config
            MemoryCacheConfig memCacheConfig = config[VNCacheExtensions.MEMORY_CACHE_CONFIG_KEY].Deserialize<MemoryCacheConfig>()!;

            if (pbase.IsDebug())
            {
                //Use the debug heap
                IUnmangedHeap newHeap = MemoryUtil.InitializeNewHeapForProcess();

                //Wrap in diag heap
                 _bufferHeap = new TrackedHeapWrapper(newHeap);
            }
            else
            {
                //Init new "private" heap to alloc buffer from
                _bufferHeap = MemoryUtil.InitializeNewHeapForProcess();
            }

            //Setup cache table
            _memCache = new BlobCacheTable(memCacheConfig.TableSize, memCacheConfig.BucketSize, _bufferHeap, null);

            /*
             * Default to json serialization by using the default
             * serializer and JSON options
             */

            JsonCacheObjectSerializer defaultSerializer = new();
            _serialzer = defaultSerializer;
            _deserialzer = defaultSerializer;

            PrintDebug(pbase.Log, memCacheConfig);
        }

        private static void PrintDebug(ILogProvider log, MemoryCacheConfig config)
        {
            long maxObjects = config.BucketSize * config.TableSize;

            long size4kMb = (maxObjects * 4096)/MB_DIVISOR;
            long size8kMb = (maxObjects * 8128)/MB_DIVISOR;
            long size16kMb = (maxObjects * 16384)/MB_DIVISOR;

            log.Debug(DEBUG_TEMPLATE, config.TableSize, config.BucketSize, maxObjects, size4kMb, size8kMb, size16kMb);
        }

        ///<inheritdoc/>
        public bool IsConnected { get; } = true;

        protected override void Free()
        {
            _memCache.Dispose();
            _bufferHeap.Dispose();
        }

        ///<inheritdoc/>
        public Task AddOrUpdateAsync<T>(string key, string? newKey, T value, CancellationToken cancellation)
        {
            return AddOrUpdateAsync(key, newKey, value, _serialzer, cancellation);
        }

        ///<inheritdoc/>
        public async Task AddOrUpdateAsync<T>(string key, string? newKey, T value, ICacheObjectSerialzer serialzer, CancellationToken cancellation)
        {
            Check();

            //Alloc serialzation buffer
            using AddOrUpdateBuffer buffer = new (_bufferHeap);

            //Serialze the value
            serialzer.Serialize(value, buffer);

            //Update object data
            await _memCache.AddOrUpdateObjectAsync(key, newKey, static b => b.GetData(), buffer, default, cancellation);
        }

        ///<inheritdoc/>
        public Task DeleteAsync(string key, CancellationToken cancellation)
        {
            Check();
            return _memCache.DeleteObjectAsync(key, cancellation).AsTask();
        }

        ///<inheritdoc/>
        public Task<T?> GetAsync<T>(string key, CancellationToken cancellation) => GetAsync<T>(key, _deserialzer, cancellation);

        ///<inheritdoc/>
        public async Task<T?> GetAsync<T>(string key, ICacheObjectDeserialzer deserializer, CancellationToken cancellation)
        {
            Check();

            IBlobCacheBucket bucket = _memCache.GetBucket(key);

            //Obtain cache handle
            using (CacheBucketHandle handle = await bucket.WaitAsync(cancellation))
            {
                //Try to read the value
                if (handle.Cache.TryGetValue(key, out CacheEntry entry))
                {
                    return (T?)deserializer.Deserialze(typeof(T), entry.GetDataSegment());
                }
            }

            return default;
        }
    }
}