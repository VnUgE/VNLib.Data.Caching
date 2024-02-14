/*
* Copyright (c) 2024 Vaughn Nugent
* 
* Library: VNLib
* Package: VNLib.Data.Caching.Providers.VNCache
* File: MemoryCache.cs 
*
* MemoryCache.cs is part of VNLib.Data.Caching.Providers.VNCache 
* which is part of the larger VNLib collection of libraries and utilities.
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

using VNLib.Utils.Memory;
using VNLib.Utils.Logging;
using VNLib.Utils.Memory.Diagnostics;
using VNLib.Data.Caching;
using VNLib.Data.Caching.ObjectCache;
using VNLib.Plugins;
using VNLib.Plugins.Extensions.Loading;

namespace VNLib.Data.Caching.Providers.VNCache
{

    [ConfigurationName(VNCacheClient.CACHE_CONFIG_KEY)]
    internal sealed class MemoryCache : VNCacheBase, IDisposable
    {
        const int MB_DIVISOR = 1000 * 1024;

        const string DEBUG_TEMPLATE =@"Configuring Memory-Only Cache
 | -----------------------------
 | Configuration:
 |   Table Size:  {ts}
 |   Bucket Size: {bs}
 |   Max Objects: {obj}
 | Max Memory Estimations:
 |   4K blocks: {4k}Mb
 |   8K blocks: {8k}Mb
 |  16K blocks: {16K}Mb
 | -----------------------------
";

        private readonly IBlobCacheTable _memCache;
        private readonly IUnmangedHeap _bufferHeap;
        private readonly BucketLocalManagerFactory? _blobCacheMemManager;

        public MemoryCache(PluginBase pbase, IConfigScope config)
            : this(
                config[VNCacheClient.MEMORY_CACHE_CONFIG_KEY].Deserialize<MemoryCacheConfig>()!,
                pbase.IsDebug(),
                pbase.Log,
                pbase.GetOrCreateSingleton<BucketLocalManagerFactory>()
            )
        { }

        public MemoryCache(MemoryCacheConfig config) : this(config, false, null, null)
        { }

        private MemoryCache(MemoryCacheConfig config, bool isDebug, ILogProvider? log, BucketLocalManagerFactory? factory) : base(config)
        {
            //Validate config
            config.Validate();

            if (isDebug)
            {
                //Use the debug heap
                IUnmangedHeap newHeap = MemoryUtil.InitializeNewHeapForProcess();

                //Wrap in diag heap
                _bufferHeap = new TrackedHeapWrapper(newHeap, true);
            }
            else
            {
                //Init new "private" heap to alloc buffer from
                _bufferHeap = MemoryUtil.InitializeNewHeapForProcess();
            }

            //Fallback to creating a local/single instance of the manager
            factory ??= _blobCacheMemManager = BucketLocalManagerFactory.Create(config.ZeroAllAllocations);

            //Setup cache table
            _memCache = new BlobCacheTable(config.TableSize, config.BucketSize, factory, null);

            PrintDebug(log, config);
        }

        private static void PrintDebug(ILogProvider? log, MemoryCacheConfig config)
        {
            long maxObjects = config.BucketSize * config.TableSize;

            long size4kMb = maxObjects * 4096/MB_DIVISOR;
            long size8kMb = maxObjects * 8128/MB_DIVISOR;
            long size16kMb = maxObjects * 16384/MB_DIVISOR;

            log?.Debug(DEBUG_TEMPLATE, config.TableSize, config.BucketSize, maxObjects, size4kMb, size8kMb, size16kMb);
        }

        public void Dispose()
        {
            _memCache.Dispose();
            _bufferHeap.Dispose();
            _blobCacheMemManager?.Dispose();
        }

        ///<inheritdoc/>
        public override object GetUnderlyingStore() => _memCache;

        ///<inheritdoc/>
        public override bool IsConnected { get; } = true;

        ///<inheritdoc/>
        public override async Task AddOrUpdateAsync<T>(string key, string? newKey, T value, ICacheObjectSerializer serialzer, CancellationToken cancellation)
        {
            //Alloc serialzation buffer
            using AddOrUpdateBuffer buffer = new (_bufferHeap);

            //Serialze the value
            serialzer.Serialize(value, buffer);

            //Update object data
            await _memCache.AddOrUpdateObjectAsync(key, newKey, static b => b.GetData(), buffer, default, cancellation);
        }

        ///<inheritdoc/>
        public override Task<bool> DeleteAsync(string key, CancellationToken cancellation)
        {
            return _memCache.DeleteObjectAsync(key, cancellation).AsTask();
        }

        ///<inheritdoc/>
        public override async Task<T> GetAsync<T>(string key, ICacheObjectDeserializer deserializer, CancellationToken cancellation)
        {
            IBlobCacheBucket bucket = _memCache.GetBucket(key);

            //Obtain lock
            IBlobCache cache = await bucket.ManualWaitAsync(cancellation);

            try
            {
                //Try to read the value
                if (cache.TryGetValue(key, out CacheEntry entry))
                {
                    return deserializer.Deserialize<T>(entry.GetDataSegment())!;
                }

                return default!;
            }
            finally
            {
                bucket.Release();
            }
        }

        ///<inheritdoc/>
        public override async Task GetAsync<T>(string key, ObjectDataSet<T> callback, T state, CancellationToken cancellation)
        {
            //Get the bucket from the desired key
            IBlobCacheBucket bucket = _memCache.GetBucket(key);

            //Obtain lock
            IBlobCache cache = await bucket.ManualWaitAsync(cancellation);

            try
            {
                //Try to read the value
                if (cache.TryGetValue(key, out CacheEntry entry))
                {
                    //Set result data
                    callback(state, entry.GetDataSegment());
                }
            }
            finally
            {
                bucket.Release();
            }
        }

        ///<inheritdoc/>
        public override Task AddOrUpdateAsync<T>(string key, string? newKey, ObjectDataGet<T> callback, T state, CancellationToken cancellation)
        {
            //Update object data
            return _memCache.AddOrUpdateObjectAsync(key, newKey, callback, state, default, cancellation).AsTask();
        }
    }
}