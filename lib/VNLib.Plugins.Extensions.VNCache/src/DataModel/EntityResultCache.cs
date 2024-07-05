/*
* Copyright (c) 2024 Vaughn Nugent
* 
* Library: VNLib
* Package: VNLib.Plugins.Extensions.VNCache
* File: EntityResultCache.cs 
*
* EntityResultCache.cs is part of VNLib.Plugins.Extensions.VNCache 
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

namespace VNLib.Plugins.Extensions.VNCache.DataModel
{
    /// <summary>
    /// Represents a cache that can store entities by their unique key
    /// using a user-provided backing store and custom request state.
    /// </summary>
    /// <typeparam name="TEntity"></typeparam>
    /// <param name="cache">The cache backing store</param>
    /// <param name="taskPolicy">Specifies how background cache tasks are handled</param>
    /// <param name="expirationPolicy">The result expiration policy</param>
    public class EntityResultCache<TEntity>(
        IEntityCache<TEntity> cache, 
        ICacheTaskPolicy taskPolicy, 
        ICacheExpirationPolicy<TEntity> expirationPolicy
    )
        where TEntity : class
    {

        /// <summary>
        /// Fetchs a result by it's request entity
        /// </summary>
        /// <param name="request">The fetch request state object</param>
        /// <param name="cancellation">A token to canel the operation</param>
        /// <param name="resultFactory">A callback generator function</param>
        /// <returns>A task the returns the result of the requested entity, or null if it was not found or provided by the backing store</returns>
        public async Task<TEntity?> FetchAsync<TRequest>(
            TRequest request, 
            Func<TRequest, CancellationToken, Task<TEntity?>> resultFactory, 
            CancellationToken cancellation = default
        ) where TRequest : IEntityCacheKey
        {
            ArgumentNullException.ThrowIfNull(request);
            ArgumentNullException.ThrowIfNull(resultFactory);
            cancellation.ThrowIfCancellationRequested();

            string key = request.GetKey();

            //try to fetch from cache
            TEntity? entity = await cache.GetAsync(key, cancellation);

            if (entity is not null)
            {
                //Check if the entity is expired
                if (expirationPolicy.IsExpired(entity))
                {
                    //Setting to null will force a cache miss
                    entity = null;
                }
            }

            if (entity is null)
            {
                //Cache miss, load from factory
                entity = await resultFactory(request, cancellation);

                if (entity is not null)
                {
                    //Notify the expiration policy that the entity was refreshed before writing back to cache
                    expirationPolicy.OnRefreshed(entity);

                    //Fresh entity was fetched from the factory so write to cache
                    Task upsert = cache.UpsertAsync(key, entity, cancellation);

                    //Allow task policy to determine how completions are observed
                    await taskPolicy.ObserveOperationAsync(upsert);
                }
            }

            return entity;
        }

        /// <summary>
        /// Removes an entity from the cache by it's request entity
        /// </summary>
        /// <param name="request">The request entity to retrieve the entity key from</param>
        /// <param name="cancellation">A token to cancel the async operation</param>
        /// <returns>A task that completes when the key is removed, based on the task policy</returns>
        public Task RemoveAsync<TRequest>(TRequest request, CancellationToken cancellation = default) 
            where TRequest : IEntityCacheKey
        {
            ArgumentNullException.ThrowIfNull(request);

            string key = request.GetKey();

            return cache.RemoveAsync(key, cancellation);
        }

        /// <summary>
        /// Removes an entity from the cache by it's request entity
        /// </summary>
        /// <param name="key">The entities unique key</param>
        /// <param name="cancellation">A token to cancel the async operation</param>
        /// <returns>A task that completes when the key is removed, based on the task policy</returns>
        public Task RemoveAsync(string key, CancellationToken cancellation = default)
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(key);

            Task remove = cache.RemoveAsync(key, cancellation);

            return taskPolicy.ObserveOperationAsync(remove);
        }
    }
}
