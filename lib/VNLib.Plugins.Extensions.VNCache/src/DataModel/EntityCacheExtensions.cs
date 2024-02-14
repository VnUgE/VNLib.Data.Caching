/*
* Copyright (c) 2024 Vaughn Nugent
* 
* Library: VNLib
* Package: VNLib.Plugins.Extensions.VNCache
* File: EntityCacheExtensions.cs 
*
* EntityCacheExtensions.cs is part of VNLib.Plugins.Extensions.VNCache 
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
using System.Threading;
using System.Threading.Tasks;
using System.Runtime.CompilerServices;

using VNLib.Data.Caching;

namespace VNLib.Plugins.Extensions.VNCache.DataModel
{

    /// <summary>
    /// Provides cache extensions for entity caching
    /// </summary>
    public static class EntityCacheExtensions
    {
        /// <summary>
        /// Gets a new <see cref="ScopedCache"/> that is backed by the current cache provider
        /// and generates 1:1 keys from the <paramref name="cacheKeyGenerator"/>
        /// </summary>
        /// <param name="cache"></param>
        /// <param name="cacheKeyGenerator">The instance that generates unique keys for a given entity id</param>
        /// <returns>The new <see cref="ScopedCache"/> instance</returns>
        public static ScopedCache GetScopedCache(this IGlobalCacheProvider cache, ICacheKeyGenerator cacheKeyGenerator) => new ScopedCacheImpl(cache, cacheKeyGenerator);

        /// <summary>
        /// Deletes an <see cref="ICacheEntity"/> from the cache from its id
        /// </summary>
        /// <typeparam name="T"></typeparam>
        /// <param name="cache"></param>
        /// <param name="entity">The entity to delete from the store</param>
        /// <param name="cancellation">A token to cancel the operation</param>
        /// <returns>A task that completes when the delete operation has compelted</returns>
        /// <exception cref="ArgumentNullException"></exception>
        public static Task<bool> RemoveAsync<T>(this IEntityCache<T> cache, T entity, CancellationToken cancellation) where T: class, ICacheEntity
        {
            _ = entity ?? throw new ArgumentNullException(nameof(entity));
            _ = cache ?? throw new ArgumentNullException(nameof(entity));
            //Delete by its id
            return cache.RemoveAsync(entity.Id, cancellation);
        }

        /// <summary>
        /// Asynchronously sets (or updates) a cached value in the backing cache store
        /// </summary>
        /// <typeparam name="T"></typeparam>
        /// <param name="cache"></param>
        /// <param name="cancellation">A token to cancel the async operation</param>
        /// <param name="entity">The entity to set at the given key</param>
        /// <returns>A task that completes when the add/update operation has compelted</returns>
        /// <exception cref="ArgumentNullException"></exception>
        public static Task UpsertAsync<T>(this IEntityCache<T> cache, T entity, CancellationToken cancellation) where T: class, ICacheEntity
        {
            _ = entity ?? throw new ArgumentNullException(nameof(entity));
            _ = cache ?? throw new ArgumentNullException(nameof(cache));

            //Add/update with its id
            return cache.UpsertAsync(entity.Id, entity, cancellation);
        }

        /// <summary>
        /// Creates an <see cref="IEntityCache{T}"/> wrapper using the current global cache provider.
        /// Understand this will share the same cache store as other stores. Consider creating a scoped cache
        /// to avoid key collisions
        /// </summary>
        /// <typeparam name="T"></typeparam>
        /// <param name="cache"></param>
        /// <param name="serialier">The entity data serializer</param>
        /// <param name="deserializer">The entity data deserializer</param>
        /// <returns>The new <see cref="IEntityCache{T}"/> wrapper instance</returns>
        /// <exception cref="ArgumentNullException"></exception>
        public static IEntityCache<T> CreateEntityCache<T>(this IGlobalCacheProvider cache, ICacheObjectSerializer serialier, ICacheObjectDeserializer deserializer) where T: class
        {
            _ = cache ?? throw new ArgumentNullException(nameof(cache));
            _ = serialier ?? throw new ArgumentNullException(nameof(serialier));
            _ = deserializer ?? throw new ArgumentNullException(nameof(deserializer));

            return new EntityCacheImpl<T>(cache, deserializer, serialier);
        }

        /// <summary>
        /// Creates an <see cref="IEntityCache{T}"/> wrapper using the current global cache provider, 
        /// with a Json serializer/deserializer
        /// </summary>
        /// <typeparam name="T"></typeparam>
        /// <param name="cache"></param>
        /// <param name="bufferSize">The default serializer buffer size</param>
        /// <returns>The new <see cref="IEntityCache{T}"/> wrapper using json serialization</returns>
        /// <exception cref="ArgumentNullException"></exception>
        public static IEntityCache<T> CreateJsonEntityCache<T>(this IGlobalCacheProvider cache, int bufferSize) where T: class
        {
            _ = cache ?? throw new ArgumentNullException(nameof(cache));
            JsonCacheObjectSerializer json = new(bufferSize);
            return CreateEntityCache<T>(cache, json, json);
        }

        /// <summary>
        /// Attemts to recover an entity from cache if possible, if a miss occurs, the 
        /// factory function is called to produce a value from a backing store. If the store 
        /// returns a result it is writen back to the cache before this method returns
        /// </summary>
        /// <typeparam name="T"></typeparam>
        /// <param name="cache"></param>
        /// <param name="id">The id of the entity to get or laod</param>
        /// <param name="factory">The factory callback function to produce a value when a cache miss occurs</param>
        /// <param name="cancellation">A token to cancel the operation</param>
        /// <returns>A task that completes by returning the entity</returns>
        /// <exception cref="ArgumentNullException"></exception>
        public static async Task<T?> GetOrLoadAsync<T>(this IEntityCache<T> cache, string id, Func<string, Task<T?>> factory, CancellationToken cancellation = default) where T : class
        {
            _ = cache ?? throw new ArgumentNullException(nameof(cache));
            _ = id ?? throw new ArgumentNullException(nameof(id));
            _ = factory ?? throw new ArgumentNullException(nameof(factory));

            //try to load the value from cache
            T? record = await cache.GetAsync(id, cancellation);

            //If record was not found in cache, load it from the factory
            if (record is null)
            {
                record = await factory(id);

                //If new record found, write to cache
                if (record is not null)
                {
                    await cache.UpsertAsync(id, record, cancellation);
                }
            }

            return record;
        }

        /// <summary>
        /// Attemts to recover an entity from cache if possible, if a miss occurs, the 
        /// factory function is called to produce a value from a backing store. If the store 
        /// returns a result it is writen back to the cache before this method returns
        /// </summary>
        /// <typeparam name="T"></typeparam>
        /// <param name="cache"></param>
        /// <param name="id">The id of the entity to get or laod</param>
        /// <param name="factory">The factory callback function to produce a value when a cache miss occurs</param>
        /// <param name="cancellation">A token to cancel the operation</param>
        /// <returns>A task that completes by returning the entity</returns>
        /// <exception cref="ArgumentException"></exception>
        /// <exception cref="ArgumentNullException"></exception>
        public static async Task<T?> GetOrLoadAsync<T>(this IEntityCache<T> cache, string id, Func<string, CancellationToken, Task<T?>> factory, CancellationToken cancellation = default) where T : class
        {
            ArgumentNullException.ThrowIfNull(cache);
            ArgumentNullException.ThrowIfNull(factory);
            ArgumentException.ThrowIfNullOrWhiteSpace(id);

            //try to load the value from cache
            T? record = await cache.GetAsync(id, cancellation);

            //If record was not found in cache, load it from the factory
            if (record is null)
            {
                record = await factory(id, cancellation);
                
                //If new record found, write to cache
                if(record is not null)
                {
                    await cache.UpsertAsync(id, record, cancellation);
                }
            }

            return record;           
        } 
      
        private sealed class EntityCacheImpl<T> : IEntityCache<T> where T : class
        {
            private readonly IGlobalCacheProvider _cacheProvider;
            private readonly ICacheObjectDeserializer _cacheObjectDeserialzer;
            private readonly ICacheObjectSerializer _cacheObjectSerialzer;

            public EntityCacheImpl(IGlobalCacheProvider cache, ICacheObjectDeserializer deserializer, ICacheObjectSerializer serializer)
            {
                _cacheProvider = cache;
                _cacheObjectDeserialzer = deserializer;
                _cacheObjectSerialzer = serializer;
            }

            ///<inheritdoc/>
            public Task<T?> GetAsync(string id, CancellationToken token = default) => _cacheProvider.GetAsync<T>(id, _cacheObjectDeserialzer, token);

            ///<inheritdoc/>
            public Task<bool> RemoveAsync(string id, CancellationToken token = default) => _cacheProvider.DeleteAsync(id, token);

            ///<inheritdoc/>
            public Task UpsertAsync(string id, T entity, CancellationToken token = default) => _cacheProvider.AddOrUpdateAsync(id, null, entity, _cacheObjectSerialzer, token);
        }

        private sealed class ScopedCacheImpl: ScopedCache
        {
            private readonly IGlobalCacheProvider Cache;

            ///<inheritdoc/>
            public override bool IsConnected
            {
                [MethodImpl(MethodImplOptions.AggressiveInlining)]
                get => Cache.IsConnected;
            }

            ///<inheritdoc/>
            protected override ICacheKeyGenerator KeyGen { get; }

            ///<inheritdoc/>
            public override ICacheObjectDeserializer DefaultDeserializer => Cache.DefaultDeserializer;

            ///<inheritdoc/>
            public override ICacheObjectSerializer DefaultSerializer => Cache.DefaultSerializer;

            public ScopedCacheImpl(IGlobalCacheProvider cache, ICacheKeyGenerator keyGen)
            {
                this.Cache = cache;
                KeyGen = keyGen;
            }

            ///<inheritdoc/>
            public override Task<bool> DeleteAsync(string key, CancellationToken cancellation)
            {
                ArgumentException.ThrowIfNullOrWhiteSpace(key);
                //Compute the key for the id
                string scoped = KeyGen.ComputedKey(key);
                return Cache.DeleteAsync(scoped, cancellation);
            }

            ///<inheritdoc/>
            public override Task<T> GetAsync<T>(string key, ICacheObjectDeserializer deserializer, CancellationToken cancellation)
            {
                ArgumentException.ThrowIfNullOrWhiteSpace(key);

                //Compute the key for the id
                string scoped = KeyGen.ComputedKey(key);

                return Cache.GetAsync<T?>(scoped, deserializer, cancellation);
            }

            ///<inheritdoc/>
            public override Task AddOrUpdateAsync<T>(string key, string? newKey, T value, ICacheObjectSerializer serialzer, CancellationToken cancellation)
            {
                ArgumentException.ThrowIfNullOrWhiteSpace(key);

                //Compute primary key from id
                string primary = KeyGen.ComputedKey(key);

                //If newkey exists, compute the secondary key
                string? secondary = newKey != null ? KeyGen.ComputedKey(newKey) : null;

                return Cache.AddOrUpdateAsync(primary, secondary, value, serialzer, cancellation);
            }
         
            ///<inheritdoc/>
            public override Task GetAsync<T>(string key, ObjectDataSet<T> callback, T state, CancellationToken cancellation)
            {
                ArgumentException.ThrowIfNullOrWhiteSpace(key);

                //Compute the key for the id
                string scoped = KeyGen.ComputedKey(key);

                return Cache.GetAsync(scoped, callback, state, cancellation);
            }

            ///<inheritdoc/>
            public override Task AddOrUpdateAsync<T>(string key, string? newKey, ObjectDataGet<T> callback, T state, CancellationToken cancellation)
            {
                ArgumentException.ThrowIfNullOrWhiteSpace(key);

                //Compute primary key from id
                string primary = KeyGen.ComputedKey(key);

                //If newkey exists, compute the secondary key
                string? secondary = newKey != null ? KeyGen.ComputedKey(newKey) : null;

                return Cache.AddOrUpdateAsync(primary, secondary, callback, state, cancellation);
            }

            ///<inheritdoc/>
            public override object GetUnderlyingStore() => Cache.GetUnderlyingStore();
        }
    }
   
}
