/*
* Copyright (c) 2023 Vaughn Nugent
* 
* Library: VNLib
* Package: VNLib.Plugins.Extensions.VNCache
* File: IEntityCache.cs 
*
* IEntityCache.cs is part of VNLib.Plugins.Extensions.VNCache 
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
    /// Represents a cache that stores referrence type entities 
    /// </summary>
    /// <typeparam name="T">The referrence entity type</typeparam>
    public interface IEntityCache<T> where T : class
    {
        /// <summary>
        /// Gets an entity from the cache by its id. Returns null if the entity is not found
        /// </summary>
        /// <param name="id">The id of the entity to retrieve from the store</param>
        /// <param name="token">A token to cancel the operation</param>
        /// <returns> The entity if found, null otherwise</returns>
        Task<T?> GetAsync(string id, CancellationToken token = default);

        /// <summary>
        /// Upserts an entity into the cache by its id. This updates an existing entity 
        /// or inserts a new one.
        /// </summary>
        /// <param name="id">The id of the entity to update</param>
        /// <param name="entity">A referrence to the entity instance to update</param>
        /// <param name="token">A token to cancel the operation</param>
        /// <returns>A task that completes when the update has completed successfully</returns>
        Task UpsertAsync(string id, T entity, CancellationToken token = default);

        /// <summary>
        /// Removes an entity from the cache by its id
        /// </summary>
        /// <param name="id">The id of the item to remove</param>
        /// <param name="token">A token to cancel delete opdation</param>
        /// <returns>A task that completes when the item has been deleted successfully</returns>
        Task<bool> RemoveAsync(string id, CancellationToken token = default);
    }
   
}
