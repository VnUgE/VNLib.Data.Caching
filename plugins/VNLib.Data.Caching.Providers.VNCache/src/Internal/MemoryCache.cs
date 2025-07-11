﻿/*
* Copyright (c) 2025 Vaughn Nugent
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
using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;

using VNLib.Utils.IO;
using VNLib.Utils.Extensions;
using VNLib.Utils.Memory;
using VNLib.Utils.Logging;
using VNLib.Utils.Memory.Diagnostics;
using VNLib.Data.Caching;
using VNLib.Data.Caching.ObjectCache;
using VNLib.Plugins;

namespace VNLib.Data.Caching.Providers.VNCache.Internal
{
   
    internal sealed class MemoryCache : VNCacheBase, IDisposable
    {
        private bool _isConnected;

        private readonly VNMemoryCacheConfig _config;
        private readonly BlobCacheTable _memCache;
        private readonly IUnmangedHeap _bufferHeap;
        private readonly BucketLocalManagerFactory? _blobCacheMemManager;
     
        internal MemoryCache(VNMemoryCacheConfig config) : base(config)
        {
            Debug.Assert(config != null);
            _config = config;

            //Assign a default memory manager if none is provided
            config.MemoryManagerFactory 
                ??= _blobCacheMemManager = BucketLocalManagerFactory.Create(config.ZeroAllAllocations);

            //Setup cache table using plugin generated memory manager
            _memCache = new BlobCacheTable(
                config.TableSize,
                config.BucketSize,
                factory: config.MemoryManagerFactory,
                persistantCache: null
            );

            /*
             * It is usually asssumed that a private heap will be slower and add to the 
             * process commited memory with little beneif except some security by 
             * using it's own heap. However, in most cases it's far more efficient to use 
             * the global heap in a thread-unsafe context like the buffer in this class. 
             * 
             * Private heaps can be more performant in memory caches because they are 
             * garunteed to be thread safe. 
             */

            if (config.UsePrivateBufferHeap)
            {               
                _bufferHeap = MemoryUtil.InitializeNewHeapForProcess(config.ZeroAllAllocations);

                if (config.IsDebug)
                {
                    // If debugging is enabled, wrap the heap in a tracked
                    // heap wrapper for debugging purposes
                    _bufferHeap = new TrackedHeapWrapper(heap: _bufferHeap, ownsHeap: true);
                }
            }
            else
            {              
                _bufferHeap = MemoryUtil.Shared;
            }
        }

        private static void PrintDebug(ILogProvider log, VNMemoryCacheConfig config)
        {
            const int MB_DIVISOR = 1000 * 1024;
            const string DEBUG_TEMPLATE = @"Configuring Memory-Only Cache
 | -----------------------------
 | Configuration:
 |   Table Size:  {ts}
 |   Bucket Size: {bs}
 |   Max Objects: {obj}
 |   Private heap: {heap}
 |   Zero alloc: {zero}
 | -----------------------------
 | Max Memory Estimations:
 |   4K blocks: {4k}Mb
 |   8K blocks: {8k}Mb
 |  16K blocks: {16K}Mb
 | -----------------------------
";

            long maxObjects = config.BucketSize * config.TableSize;

            long size4kMb = maxObjects * 4096/MB_DIVISOR;
            long size8kMb = maxObjects * 8128/MB_DIVISOR;
            long size16kMb = maxObjects * 16384/MB_DIVISOR;

            log.Debug(
                DEBUG_TEMPLATE, 
                config.TableSize, 
                config.BucketSize, 
                maxObjects,
                config.UsePrivateBufferHeap,
                config.ZeroAllAllocations,
                size4kMb, 
                size8kMb, 
                size16kMb
            );
        }

        public void Dispose()
        {
            _memCache.Dispose();
            _blobCacheMemManager?.Dispose();

            // only dispose the heap if it's a private heap
            if (_config.UsePrivateBufferHeap)
            {
                _bufferHeap.Dispose();
            }
        }

        ///<inheritdoc/>
        public override async Task RunAsync(PluginBase? plugin, ILogProvider operationLog, CancellationToken exitToken)
        {
            if (plugin is not null)
            {
                PrintDebug(plugin.Log, _config);
            }

            // Set connected flag
            _isConnected = true;

            /*
             * Just a dummy task that waits until the token is cancelled to exit
             */
            await exitToken.WaitHandle.NoSpinWaitAsync(Timeout.Infinite);

            // Clear connected flag
            _isConnected = false;
        }

        ///<inheritdoc/>
        public override object GetUnderlyingStore() => _memCache;

        ///<inheritdoc/>
        public override bool IsConnected => _isConnected;

        ///<inheritdoc/>
        public override async Task AddOrUpdateAsync<T>(string key, string? newKey, T value, ICacheObjectSerializer serialzer, CancellationToken cancellation)
        {
            //Alloc serialzation buffer
            using VnMemoryStream buffer = new (_bufferHeap);

            //Serialze the value
            serialzer.Serialize(value, buffer);

            //Update object data
            await _memCache.AddOrUpdateObjectAsync(
                key, 
                newKey, 
                static b => b.AsSpan(), 
                state: buffer, 
                time: default, 
                cancellation
            );
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
                return cache.TryGetValue(key, out CacheEntry entry) 
                    ? deserializer.Deserialize<T>(entry.GetDataSegment())! 
                    : default!;
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
            return _memCache.AddOrUpdateObjectAsync(
                key, 
                newKey, 
                callback, 
                state, 
                time: default, 
                cancellation
            ).AsTask();
        }
    }
}