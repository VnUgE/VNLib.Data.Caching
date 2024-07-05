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

using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace VNLib.Plugins.Extensions.VNCache.DataModel
{
    /// <summary>
    /// A cache proxy that sits transparently between a backing store and the caller
    /// to cache unique entities
    /// </summary>
    /// <typeparam name="TEntity"></typeparam>
    /// <param name="store">The backing entity data store</param>
    /// <param name="cache">The entity cache used to fetch</param>
    public class TransparentEntityCache<TEntity>(IEntityStore<TEntity> store, EntityResultCache<TEntity> cache) : IEntityStore<TEntity> 
        where TEntity : class
    {

        ///<inheritdoc/>
        public Task<TEntity?> GetAsync<TRequest>(TRequest request, CancellationToken cancellation = default) 
            where TRequest : IEntityCacheKey
        {
            return cache.FetchAsync(request, store.GetAsync, cancellation);
        }

        ///<inheritdoc/>
        public Task<bool> RemoveAsync<TRequest>(TRequest request, CancellationToken cancellation = default)
            where TRequest : IEntityCacheKey
        {
            Task<bool> _fromCache = cache.RemoveAsync(request, cancellation)
                .ContinueWith(static (_) => true, TaskScheduler.Default);

            Task<bool> _fromStore = store.RemoveAsync(request, cancellation);

            return Task.WhenAll(_fromCache, _fromStore)
                .ContinueWith(static (t) => t.Result.All(static r => r), TaskScheduler.Default);
        }

        ///<inheritdoc/>
        public Task UpsertAsync<TRequest>(TRequest request, TEntity entity, CancellationToken cancellation = default)
            where TRequest : IEntityCacheKey
        {
            //Remove key from cache but push update to store
            Task _fromCache = cache.RemoveAsync(request, cancellation);

            Task _fromStore = store.UpsertAsync(request, entity, cancellation);

            return Task.WhenAll(_fromCache, _fromStore);
        }
    }
}
