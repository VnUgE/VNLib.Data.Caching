/*
* Copyright (c) 2022 Vaughn Nugent
* 
* Library: VNLib
* Package: VNLib.Data.Caching
* File: IGlobalCacheProvider.cs 
*
* IGlobalCacheProvider.cs is part of VNLib.Data.Caching which is part of the larger 
* VNLib collection of libraries and utilities.
*
* VNLib.Data.Caching is free software: you can redistribute it and/or modify 
* it under the terms of the GNU Affero General Public License as 
* published by the Free Software Foundation, either version 3 of the
* License, or (at your option) any later version.
*
* VNLib.Data.Caching is distributed in the hope that it will be useful,
* but WITHOUT ANY WARRANTY; without even the implied warranty of
* MERCHANTABILITY or FITNESS FOR A PARTICULAR PURPOSE. See the
* GNU Affero General Public License for more details.
*
* You should have received a copy of the GNU Affero General Public License
* along with this program.  If not, see https://www.gnu.org/licenses/.
*/

using System.Threading;
using System.Threading.Tasks;

namespace VNLib.Data.Caching
{
    /// <summary>
    /// A global cache provider interface
    /// </summary>
    public interface IGlobalCacheProvider
    {
        /// <summary>
        /// Gets a value that indicates if the cache provider is currently available
        /// </summary>
        bool IsConnected { get;  }

        /// <summary>
        /// Asynchronously gets a value from the backing cache store
        /// </summary>
        /// <typeparam name="T"></typeparam>
        /// <param name="key">The key identifying the object to recover from cache</param>
        /// <param name="cancellation">A token to cancel the async operation</param>
        /// <returns>The value if found, or null if it does not exist in the store</returns>
        Task<T?> GetAsync<T>(string key, CancellationToken cancellation);

        /// <summary>
        /// Asynchronously sets (or updates) a cached value in the backing cache store
        /// </summary>
        /// <typeparam name="T"></typeparam>
        /// <param name="key">The key identifying the object to recover from cache</param>
        /// <param name="newKey">An optional key that will be changed for the new object</param>
        /// <param name="cancellation">A token to cancel the async operation</param>
        /// <param name="value">The value to set at the given key</param>
        /// <returns>A task that completes when the update operation has compelted</returns>
        Task AddOrUpdateAsync<T>(string key, string? newKey, T value, CancellationToken cancellation);

        /// <summary>
        /// Asynchronously deletes an item from cache by its key
        /// </summary>
        /// <param name="key">The key identifying the item to delete</param>
        /// <param name="cancellation">A token to cancel the async operation</param>
        /// <returns>A task that completes when the delete operation has compelted</returns>
        Task DeleteAsync(string key, CancellationToken cancellation);
    }
}