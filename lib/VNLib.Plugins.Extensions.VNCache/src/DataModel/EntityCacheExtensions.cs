/*
* Copyright (c) 2023 Vaughn Nugent
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
        public static Task DeleteAsync<T>(this IGlobalCacheProvider cache, T entity, CancellationToken cancellation) where T: class, ICacheEntity
        {
            _ = entity ?? throw new ArgumentNullException(nameof(entity));
            _ = cache ?? throw new ArgumentNullException(nameof(entity));
            //Delete by its id
            return cache.DeleteAsync(entity.Id, cancellation);
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
        public static Task AddOrUpdateAsync<T>(this IGlobalCacheProvider cache, T entity, CancellationToken cancellation) where T: class, ICacheEntity
        {
            _ = entity ?? throw new ArgumentNullException(nameof(entity));
            _ = cache ?? throw new ArgumentNullException(nameof(cache));

            //Add/update with its id
            return cache.AddOrUpdateAsync(entity.Id, null, entity, cancellation);
        }
     

        private sealed class ScopedCacheImpl: ScopedCache
        {
            private readonly IGlobalCacheProvider cache;

            public override bool IsConnected
            {
                [MethodImpl(MethodImplOptions.AggressiveInlining)]
                get => cache.IsConnected;
            }


            protected override ICacheKeyGenerator KeyGen { get; }

            public ScopedCacheImpl(IGlobalCacheProvider cache, ICacheKeyGenerator keyGen)
            {
                this.cache = cache;
                KeyGen = keyGen;
            }

            public override Task AddOrUpdateAsync<T>(string key, string? newKey, T value, CancellationToken cancellation)
            {
                _ = key ?? throw new ArgumentNullException(nameof(key));

                //Compute primary key from id
                string primary = KeyGen.ComputedKey(key);

                //If newkey exists, compute the secondary key
                string? secondary = newKey != null ? KeyGen.ComputedKey(newKey) : null;

                return cache.AddOrUpdateAsync(primary, secondary, value, cancellation);
            }

            public override Task DeleteAsync(string key, CancellationToken cancellation)
            {
                _ = key ?? throw new ArgumentNullException(nameof(key));
                //Compute the key for the id
                string scoped = KeyGen.ComputedKey(key);
                return cache.DeleteAsync(scoped, cancellation);
            }

            public override Task<T> GetAsync<T>(string key, CancellationToken cancellation)
            {
                _ = key ?? throw new ArgumentNullException(nameof(key));

                //Compute the key for the id
                string scoped = KeyGen.ComputedKey(key);

                return cache.GetAsync<T?>(scoped, cancellation);
            }

            public override Task<T> GetAsync<T>(string key, ICacheObjectDeserialzer deserializer, CancellationToken cancellation)
            {
                _ = key ?? throw new ArgumentNullException(nameof(key));

                //Compute the key for the id
                string scoped = KeyGen.ComputedKey(key);

                return cache.GetAsync<T?>(scoped, deserializer, cancellation);
            }

            public override Task AddOrUpdateAsync<T>(string key, string? newKey, T value, ICacheObjectSerialzer serialzer, CancellationToken cancellation)
            {
                _ = key ?? throw new ArgumentNullException(nameof(key));

                //Compute primary key from id
                string primary = KeyGen.ComputedKey(key);

                //If newkey exists, compute the secondary key
                string? secondary = newKey != null ? KeyGen.ComputedKey(newKey) : null;

                return cache.AddOrUpdateAsync(primary, secondary, value, serialzer, cancellation);
            }
        }
    }
   
}
