﻿/*
* Copyright (c) 2025 Vaughn Nugent
* 
* Library: VNLib
* Package: VNLib.Data.Caching.Providers.VNCache
* File: RemoteBackedMemoryCache.cs 
*
* RemoteBackedMemoryCache.cs is part of VNLib.Data.Caching.Providers.VNCache 
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
using System.Linq;
using System.Buffers;
using System.Threading;
using System.Threading.Tasks;
using System.Runtime.CompilerServices;

using VNLib.Utils;
using VNLib.Utils.Memory;
using VNLib.Utils.Logging;
using VNLib.Utils.Extensions;
using VNLib.Data.Caching;
using VNLib.Data.Caching.ObjectCache;
using VNLib.Plugins;

namespace VNLib.Data.Caching.Providers.VNCache.Internal
{

    /*
     * A combined cache object that uses the blob cache data structures
     * from the ObjectCache server library to implement similar memory cache
     * features. All update operations are write-through operations, and a timer
     * may be scheduled to refresh memorycache against the server (eventually) 
     * 
     * Memory cache is destroyed when the connection to the cache server is
     * lost or is exiting
     */

    internal sealed class RemoteBackedMemoryCache : VNCacheBase, IDisposable
    {
        private readonly VNMemoryCacheConfig _cacheConfig;
        private readonly BlobCacheTable _memCache;
        private readonly IInternalCacheClient _backing;
        private readonly IUnmangedHeap _bufferHeap;
        private readonly BucketLocalManagerFactory? _blobCacheMemManager;       

        internal RemoteBackedMemoryCache(VNMemoryCacheConfig config, IInternalCacheClient backingStore)
            :base(config)
        {
            ArgumentNullException.ThrowIfNull(config);
            ArgumentNullException.ThrowIfNull(backingStore);

            _cacheConfig = config;
            _backing = backingStore;

            /*
             * If no buffer factory was supplied, we can create one, but it has to be 
             * disposed manually on exit. If one was supplied, we can use it but we do not
             * manage it's lifetime
             */
         
            config.MemoryManagerFactory
                ??= _blobCacheMemManager = BucketLocalManagerFactory.Create(config.ZeroAllAllocations);
          
            _memCache = new BlobCacheTable(
                config.TableSize,
                config.BucketSize,
                factory: config.MemoryManagerFactory,
                persistantCache: null
            );

            //If backing store is a VnCacheClient, steal it's buffer heap
            _bufferHeap = backingStore is FBMCacheClient client 
                ? client.BufferHeap 
                : MemoryUtil.Shared;           
        }

        void IDisposable.Dispose()
        {
            //Dispose of the memory cache
            _memCache.Dispose();
            _blobCacheMemManager?.Dispose();

            if (_backing is IDisposable disposable)
            {
                disposable.Dispose();
            }
        }

        ///<inheritdoc/>
        public override async Task RunAsync(PluginBase? plugin, ILogProvider clientLog, CancellationToken cancellation)
        {
            Task backingTask = _backing.RunAsync(plugin, clientLog, cancellation);
            Task intervalTask = RunIntervalAsync(clientLog, cancellation);

            await Task.WhenAny(backingTask, intervalTask);
        }

        private async Task RunIntervalAsync(ILogProvider clientLog, CancellationToken cancellation)
        {
            /*
             * Runs background work to refresh the cache on an interval
             */

            clientLog.Debug("Refresh interval scheduled at {interval}", _cacheConfig.RefreshInterval);

            try
            {
                while (true)
                {
                    await Task.Delay(_cacheConfig.RefreshInterval, cancellation);

                    await OnIntervalAsync(clientLog, cancellation);
                }
            }
            //Its normal to throw when the plugin exits or is cancelled
            catch (TaskCanceledException)
            { }
        }        

        ///<inheritdoc/>
        public override object GetUnderlyingStore() => _backing.GetUnderlyingStore();

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private void CheckConnected()
        {
            if (!_backing.IsConnected)
            {
                throw new InvalidOperationException("The client is not connected to the remote cache");
            }
        }

        ///<inheritdoc/>
        public override bool IsConnected => _backing.IsConnected;

        ///<inheritdoc/>
        public override Task<bool> DeleteAsync(string key, CancellationToken cancellation)
        {
            CheckConnected();

            //Delete the object from 
            Task<bool> local = _memCache.DeleteObjectAsync(key, cancellation).AsTask();
            Task<bool> remote = _backing.DeleteAsync(key, cancellation);

            //task when both complete
            return Task.WhenAll(local, remote)
                .ContinueWith(static p => p.Result.First(), TaskScheduler.Default);
        }

        ///<inheritdoc/>
        public override async Task<T> GetAsync<T>(string key, ICacheObjectDeserializer deserializer, CancellationToken cancellation)
        {
            _ = deserializer ?? throw new ArgumentNullException(nameof(deserializer));

            GetStateResult<T?> state = new()
            {
                Deserialzer = deserializer,
                Value = default!
            };

            //Try to the object from the cache and if found, deserialize it and store the result
            await GetAsync(key, static (r, data) => r.SetState(data), state, cancellation);

            return state.Value;
        }

        ///<inheritdoc/>
        public override async Task GetAsync<T>(string key, ObjectDataSet<T> setter, T state, CancellationToken cancellation)
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(key);
            ArgumentNullException.ThrowIfNull(setter);
     
            CheckConnected();

            IBlobCacheBucket bucket = _memCache.GetBucket(key);

            //Obtain cache handle
            using (CacheBucketHandle handle = await bucket.WaitAsync(cancellation))
            {
                //Try to read the value
                if (handle.Cache.TryGetValue(key, out CacheEntry entry))
                {
                    setter(state, entry.GetDataSegment());
                    return;
                }
            }
            /*
             * Can't avoid a double copy because we need to read the data from cache in order to store 
             * a local copy to update memcache
             */

            //Alloc buffer from client heap
            using ObjectGetBuffer getBuffer = new(_bufferHeap);

            //Get the object from the server
            await _backing.GetAsync(key, static (b, data) => b.SetData(data), getBuffer, cancellation);

            //See if object data was set
            if (!getBuffer.GetData().IsEmpty)
            {
                //Update local cache
                await _memCache.AddOrUpdateObjectAsync(key, null, static b => b.GetData(), getBuffer, DateTime.UtcNow, CancellationToken.None);

                //Invoket the setter
                setter(state, getBuffer.GetData());
            }
        }

        ///<inheritdoc/>
        public override async Task AddOrUpdateAsync<T>(string key, string? newKey, T value, ICacheObjectSerializer serialzer, CancellationToken cancellation)
        {
            CheckConnected();

            //Alloc serialzation buffer
            using AddOrUpdateBuffer buffer = new (_bufferHeap);

            //Serialze the value
            serialzer.Serialize(value, buffer);

            await AddOrUpdateAsync(key, newKey, static p => p.GetData(), buffer, cancellation);
        }

        ///<inheritdoc/>
        public override async Task AddOrUpdateAsync<T>(string key, string? newKey, ObjectDataGet<T> callback, T state, CancellationToken cancellation)
        {
            CheckConnected();

            DateTime currentTime = DateTime.UtcNow;

            try
            {
                //Update remote first, and if exceptions are raised, do not update local cache
                await _backing.AddOrUpdateAsync(key, newKey, callback, state, cancellation);

                //Safe to update local cache
                await _memCache.AddOrUpdateObjectAsync(key, newKey, callback, state, currentTime, CancellationToken.None);
            }
            catch
            {
                //Remove local cache if exception occurs
                await _memCache.DeleteObjectAsync(key, CancellationToken.None);
                throw;
            }
        }

        private async Task OnIntervalAsync(ILogProvider log, CancellationToken cancellationToken)
        {
            if (!IsConnected)
            {
                return;
            }

            //Get buckets 
            IBlobCacheBucket[] buckets = _memCache.ToArray();

            foreach (IBlobCacheBucket bucket in buckets)
            {
                //enter bucket lock
                using CacheBucketHandle handle = await bucket.WaitAsync(cancellationToken);

                //Prune expired entires
                PruneExpired(handle.Cache);
            }
        }

        private void PruneExpired(IBlobCache cache)
        {
            DateTime current = DateTime.UtcNow;

            //Enumerate all cache entires to determine if they have expired
            string[] expired = (from ec in cache
                                where ec.Value.GetTime().Add(_cacheConfig.MaxCacheAge) < current
                                select ec.Key)
                                .ToArray();

            //Remove expired entires
            for (int i = 0; i < expired.Length; i++)
            {
                cache.Remove(expired[i]);
            }
        }

        /*
         * Stores temporary state for a cache get operation
         * that requires a deserializer to return it to
         * object form
         */
        private sealed class GetStateResult<T>
        {
            public T? Value;
            public ICacheObjectDeserializer? Deserialzer;

            public void SetState(ReadOnlySpan<byte> data)
            {
                Value = Deserialzer!.Deserialize<T>(data);
            }
        }

        /*
         * A buffer to store object data on a cache get
         */
        private sealed class ObjectGetBuffer : VnDisposeable
        {
            private IMemoryHandle<byte>? _buffer;
            private readonly IUnmangedHeap _heap;

            public ObjectGetBuffer(IUnmangedHeap heap)
            {
                _heap = heap;
            }

            public ReadOnlySpan<byte> GetData()
            {
                return _buffer == null ? ReadOnlySpan<byte>.Empty : _buffer.Span;
            }

            public void SetData(ReadOnlySpan<byte> data)
            {
                //Alloc a buffer from the supplied data
                _buffer = data.IsEmpty ? null : _heap.AllocAndCopy(data);
            }

            protected override void Free()
            {
                //Free buffer
                _buffer?.Dispose();
            }
        }

    }
}