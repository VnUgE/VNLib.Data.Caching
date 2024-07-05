/*
* Copyright (c) 2024 Vaughn Nugent
* 
* Library: VNLib
* Package: VNLib.Plugins.Extensions.VNCache
* File: IEntityStore.cs 
*
* IEntityStore.cs is part of VNLib.Plugins.Extensions.VNCache 
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

using System.Threading;
using System.Threading.Tasks;

namespace VNLib.Plugins.Extensions.VNCache.DataModel
{
    /// <summary>
    /// An instance that stores entities which can be fetched, updated, or removed
    /// </summary>
    /// <typeparam name="TEntity">The entity result type</typeparam>
    public interface IEntityStore<TEntity>
    {
        /// <summary>
        /// Fetches an entity from the store by it's request entity state object
        /// </summary>
        /// <param name="request">The request state object</param>
        /// <param name="cancellation">A token to cancel the operation</param>
        /// <returns>A task that yields the entity object if it exists</returns>
        Task<TEntity?> GetAsync<TRequest>(TRequest request, CancellationToken cancellation = default) where TRequest : IEntityCacheKey;

        /// <summary>
        /// Updates or inserts an entity into the store
        /// </summary>
        /// <param name="entity">The entity instance to store in the database</param>
        /// <param name="request">The request state object</param>
        /// <param name="cancellation">A token to cancel the operation</param>
        /// <returns>A task that completes when the upsert operation has completed</returns>
        Task UpsertAsync<TRequest>(TRequest request, TEntity entity, CancellationToken cancellation = default) where TRequest : IEntityCacheKey;

        /// <summary>
        /// Removes an entity from the store
        /// </summary>
        /// <param name="request">The request state object</param>
        /// <param name="cancellation">A token to cancel the operation</param>
        /// <returns>A task that completes with the result of the delete operation</returns>
        Task<bool> RemoveAsync<TRequest>(TRequest request, CancellationToken cancellation = default) where TRequest : IEntityCacheKey;
    }
}
