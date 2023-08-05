/*
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
using System.Linq;
using System.Buffers;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using System.Runtime.CompilerServices;

using VNLib.Utils;
using VNLib.Utils.Memory;
using VNLib.Utils.Logging;
using VNLib.Utils.Extensions;
using VNLib.Data.Caching;
using VNLib.Data.Caching.ObjectCache;
using VNLib.Plugins.Extensions.Loading;
using VNLib.Plugins.Extensions.Loading.Events;

namespace VNLib.Plugins.Extensions.VNCache
{

    /*
     * A combinaed cache object that uses the blob cache data structures
     * from the ObjectCache server library to implement similar memory cache
     * features. All update operations are write-through operations, and a timer
     * may be scheduled to refresh memorycache against the server (eventually) 
     * 
     * Memory cache is destroyed when the connection to the cache server is
     * lost or is exiting
     */

    [ConfigurationName(VNCacheExtensions.CACHE_CONFIG_KEY)]
    internal sealed class RemoteBackedMemoryCache : VnCacheClient, IIntervalScheduleable
    {
        private readonly MemoryCacheConfig _cacheConfig;
        private readonly ICacheObjectSerialzer _serialzer;
        private readonly ICacheObjectDeserialzer _deserialzer;
        private readonly IBlobCacheTable _memCache;

        public RemoteBackedMemoryCache(PluginBase plugin, IConfigScope config) : base(plugin, config)
        {
            //Get nested memory cache config
            MemoryCacheConfig? memCacheConfig = config[VNCacheExtensions.MEMORY_CACHE_CONFIG_KEY].Deserialize<MemoryCacheConfig>();

            _ = memCacheConfig ?? throw new ArgumentNullException(VNCacheExtensions.MEMORY_CACHE_CONFIG_KEY, "Missing required memory configuration variable");

            memCacheConfig.Validate();

            //Setup cache table
            _memCache = new BlobCacheTable(memCacheConfig.TableSize, memCacheConfig.BucketSize, Client.Config.BufferHeap, null);

            _cacheConfig = memCacheConfig;

            /*
             * Default to json serialization by using the default
             * serializer and JSON options
             */

            JsonCacheObjectSerializer defaultSerializer = new();
            _serialzer = defaultSerializer;
            _deserialzer = defaultSerializer;

            //Schedule cache purge
            if (memCacheConfig.RefreshInterval > TimeSpan.Zero)
            {
                plugin.ScheduleInterval(this, memCacheConfig.RefreshInterval);
            }
        }

        public RemoteBackedMemoryCache(VnCacheClientConfig client, MemoryCacheConfig memCache, ILogProvider? debugLog):base(client, debugLog)
        {
            //Setup mem cache table
            _memCache = new BlobCacheTable(memCache.TableSize, memCache.BucketSize, Client.Config.BufferHeap, null);

            _cacheConfig = memCache;

            /*
             * Default to json serialization by using the default
             * serializer and JSON options
             */

            JsonCacheObjectSerializer defaultSerializer = new();
            _serialzer = defaultSerializer;
            _deserialzer = defaultSerializer;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private void CheckConnected()
        {
            if (!IsConnected)
            {
                throw new InvalidOperationException("The client is not connected to the remote cache");
            }
        }

        public override async Task DoWorkAsync(ILogProvider pluginLog, CancellationToken exitToken)
        {
            //Cleanup 
            try
            {
                await base.DoWorkAsync(pluginLog, exitToken);
            }
            finally 
            {
                _memCache.Dispose();
            }
        }

        ///<inheritdoc/>
        public override Task AddOrUpdateAsync<T>(string key, string? newKey, T value, CancellationToken cancellation) 
            => AddOrUpdateAsync(key, newKey, value, _serialzer, cancellation);

        ///<inheritdoc/>
        public override Task DeleteAsync(string key, CancellationToken cancellation)
        {
            CheckConnected();

            //Delete the object from 
            Task local = _memCache.DeleteObjectAsync(key, cancellation).AsTask();
            Task remote = Client.DeleteObjectAsync(key, cancellation);
            
            //task when both complete
            return Task.WhenAll(local, remote);
        }

        ///<inheritdoc/>
        public override Task<T> GetAsync<T>(string key, CancellationToken cancellation) => GetAsync<T>(key, _deserialzer, cancellation);

        ///<inheritdoc/>
        public override async Task<T> GetAsync<T>(string key, ICacheObjectDeserialzer deserializer, CancellationToken cancellation)
        {
            CheckConnected();

            Type objType = typeof(T);

            IBlobCacheBucket bucket = _memCache.GetBucket(key);

            //Obtain cache handle
            using (CacheBucketHandle handle = await bucket.WaitAsync(cancellation))
            {
                //Try to read the value
                if (handle.Cache.TryGetValue(key, out CacheEntry entry))
                {
                    return (T?)deserializer.Deserialze(objType, entry.GetDataSegment());
                }
            }

            //Alloc buffer from client heap
            using ObjectGetBuffer getBuffer = new(Client.Config.BufferHeap);

            //Get the object from the server
            await Client.GetObjectAsync(key, getBuffer, cancellation);

            //See if object data was set
            if (getBuffer.GetData().IsEmpty)
            {
                return default;
            }

            //Update local cache
            await _memCache.AddOrUpdateObjectAsync(key, null, static b => b.GetData(), getBuffer, DateTime.UtcNow, CancellationToken.None);

            //Deserialze the entity
            return (T)deserializer.Deserialze(objType, getBuffer.GetData());
        }

        ///<inheritdoc/>
        public override async Task AddOrUpdateAsync<T>(string key, string? newKey, T value, ICacheObjectSerialzer serialzer, CancellationToken cancellation)
        {
            CheckConnected();

            //Alloc serialzation buffer
            using AddOrUpdateBuffer buffer = new (Client.Config.BufferHeap);

            //Serialze the value
            serialzer.Serialize(value, buffer);

            //Call update on raw data
            await AddOrUpdateAsync(key, newKey, buffer, cancellation);
        }

        ///<inheritdoc/>
        public override async Task GetAsync(string key, IObjectData rawData, CancellationToken cancellation)
        {
            CheckConnected();
         
            IBlobCacheBucket bucket = _memCache.GetBucket(key);

            //Obtain cache handle
            using (CacheBucketHandle handle = await bucket.WaitAsync(cancellation))
            {
                //Try to read the value
                if (handle.Cache.TryGetValue(key, out CacheEntry entry))
                {
                    rawData.SetData(entry.GetDataSegment());
                    return;
                }
            }

            //Get the object from the server
            await Client.GetObjectAsync(key, rawData, cancellation);

            //See if object data was set
            if (rawData.GetData().IsEmpty)
            {
                return;
            }

            //Update local cache
            await _memCache.AddOrUpdateObjectAsync(key, null, static b => b.GetData(), rawData, DateTime.UtcNow, CancellationToken.None);
        }

        ///<inheritdoc/>
        public override async Task AddOrUpdateAsync(string key, string? newKey, IObjectData rawData, ICacheObjectSerialzer serialzer, CancellationToken cancellation)
        {
            CheckConnected();

            DateTime currentTime = DateTime.UtcNow;

            try
            {
                //Update remote first, and if exceptions are raised, do not update local cache
                await Client.AddOrUpdateObjectAsync(key, newKey, rawData, cancellation);

                //Safe to update local cache
                await _memCache.AddOrUpdateObjectAsync(key, newKey, static b => b.GetData(), rawData, currentTime, CancellationToken.None);
            }
            catch
            {
                //Remove local cache if exception occurs
                await _memCache.DeleteObjectAsync(key, CancellationToken.None);
                throw;
            }
        }

        async Task IIntervalScheduleable.OnIntervalAsync(ILogProvider log, CancellationToken cancellationToken)
        {
            if(!IsConnected)
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
            for(int i = 0; i < expired.Length; i++)
            {
                cache.Remove(expired[i]);
            }

            Client.Config.DebugLog?.Debug("Cleaned {mc} expired memory cache elements", expired.Length);
        }

        /*
         * A buffer to store object data on a cache get
         */
        private sealed class ObjectGetBuffer : VnDisposeable, IObjectData
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