/*
* Copyright (c) 2022 Vaughn Nugent
* 
* Library: VNLib
* Package: VNLib.Data.Caching.Global
* File: IGlobalCacheProvider.cs 
*
* IGlobalCacheProvider.cs is part of VNLib.Data.Caching.Global which is part of the larger 
* VNLib collection of libraries and utilities.
*
* VNLib.Data.Caching.Global is free software: you can redistribute it and/or modify 
* it under the terms of the GNU General Public License as published
* by the Free Software Foundation, either version 2 of the License,
* or (at your option) any later version.
*
* VNLib.Data.Caching.Global is distributed in the hope that it will be useful,
* but WITHOUT ANY WARRANTY; without even the implied warranty of
* MERCHANTABILITY or FITNESS FOR A PARTICULAR PURPOSE. See the GNU 
* General Public License for more details.
*
* You should have received a copy of the GNU General Public License 
* along with VNLib.Data.Caching.Global. If not, see http://www.gnu.org/licenses/.
*/

namespace VNLib.Data.Caching.Global
{
    /// <summary>
    /// An interface that a cache provoider must impelement to provide data caching to the 
    /// <see cref="GlobalDataCache"/> environment
    /// </summary>
    public interface IGlobalCacheProvider
    {
        /// <summary>
        /// Gets a value that indicates if the cache provider is currently available
        /// </summary>
        public bool IsConnected { get;  }

        /// <summary>
        /// Asynchronously gets a value from the backing cache store
        /// </summary>
        /// <typeparam name="T"></typeparam>
        /// <param name="key">The key identifying the object to recover from cache</param>
        /// <returns>The value if found, or null if it does not exist in the store</returns>
        Task<T?> GetAsync<T>(string key);

        /// <summary>
        /// Asynchronously sets (or updates) a cached value in the backing cache store
        /// </summary>
        /// <typeparam name="T"></typeparam>
        /// <param name="key">The key identifying the object to recover from cache</param>
        /// <param name="value">The value to set at the given key</param>
        /// <returns>A task that completes when the update operation has compelted</returns>
        Task SetAsync<T>(string key, T value);

        /// <summary>
        /// Asynchronously deletes an item from cache by its key
        /// </summary>
        /// <param name="key">The key identifying the item to delete</param>
        /// <returns>A task that completes when the delete operation has compelted</returns>
        Task DeleteAsync(string key);
    }
}