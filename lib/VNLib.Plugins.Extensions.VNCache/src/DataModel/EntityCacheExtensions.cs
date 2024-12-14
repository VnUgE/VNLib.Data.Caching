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
            ArgumentNullException.ThrowIfNull(entity);
            ArgumentNullException.ThrowIfNull(cache);
          
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
            ArgumentNullException.ThrowIfNull(cache);
            ArgumentNullException.ThrowIfNull(serialier);
            ArgumentNullException.ThrowIfNull(deserializer);

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
            ArgumentNullException.ThrowIfNull(cache);

            JsonCacheObjectSerializer json = new(bufferSize);
            return CreateEntityCache<T>(cache, json, json);
        }

        /// <summary>
        /// Creates a new <see cref="EntityCacheBuilder{TEntity}"/> instance for building a
        /// new <see cref="EntityResultCache{TEntity}"/>
        /// </summary>
        /// <typeparam name="TEntity">The result entity type</typeparam>
        /// <param name="cache"></param>
        /// <returns></returns>
        /// <exception cref="ArgumentNullException"></exception>
        public static EntityCacheBuilder<TEntity> CreateResultCache<TEntity>(this IEntityCache<TEntity> cache)
            where TEntity : class
        {
            ArgumentNullException.ThrowIfNull(cache);
            return new EntityCacheBuilder<TEntity>(cache);
        }
        private sealed class EntityCacheImpl<T>(IGlobalCacheProvider cache, ICacheObjectDeserializer deserializer, ICacheObjectSerializer serializer) 
            : IEntityCache<T> where T : class
        {
            private readonly IGlobalCacheProvider _cacheProvider = cache;
            private readonly ICacheObjectDeserializer _cacheObjectDeserialzer = deserializer;
            private readonly ICacheObjectSerializer _cacheObjectSerialzer = serializer;

            ///<inheritdoc/>
            public Task<T?> GetAsync(string id, CancellationToken token = default) 
                => _cacheProvider.GetAsync<T>(id, _cacheObjectDeserialzer, token);

            ///<inheritdoc/>
            public Task<bool> RemoveAsync(string id, CancellationToken token = default) 
                => _cacheProvider.DeleteAsync(id, token);

            ///<inheritdoc/>
            public Task UpsertAsync(string id, T entity, CancellationToken token = default) 
                => _cacheProvider.AddOrUpdateAsync(id, newKey: null, entity, _cacheObjectSerialzer, token);
        }

        private sealed class ScopedCacheImpl(IGlobalCacheProvider cache, ICacheKeyGenerator keyGen) : ScopedCache
        {
            private readonly IGlobalCacheProvider Cache = cache;

            ///<inheritdoc/>
            public override bool IsConnected
            {
                [MethodImpl(MethodImplOptions.AggressiveInlining)]
                get => Cache.IsConnected;
            }

            ///<inheritdoc/>
            protected override ICacheKeyGenerator KeyGen { get; } = keyGen;

            ///<inheritdoc/>
            public override ICacheObjectDeserializer DefaultDeserializer => Cache.DefaultDeserializer;

            ///<inheritdoc/>
            public override ICacheObjectSerializer DefaultSerializer => Cache.DefaultSerializer;

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
