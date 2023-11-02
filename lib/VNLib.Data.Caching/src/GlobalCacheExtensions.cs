/*
* Copyright (c) 2023 Vaughn Nugent
* 
* Library: VNLib
* Package: VNLib.Data.Caching
* File: GlobalCacheExtensions.cs 
*
* GlobalCacheExtensions.cs is part of VNLib.Data.Caching which is part of the larger 
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

using System;
using System.Threading;
using System.Threading.Tasks;

#pragma warning disable CA1062 // Validate arguments of public methods

namespace VNLib.Data.Caching
{
    /// <summary>
    /// Exports extension methods for the <see cref="IGlobalCacheProvider"/> interface
    /// </summary>
    public static class GlobalCacheExtensions
    {
        /// <summary>
        /// Asynchronously gets a value from the backing cache store and writes it to the 
        /// supplied data buffer
        /// </summary>
        /// <param name="cache"></param>
        /// <param name="key">The key identifying the object to recover from cache</param>
        /// <param name="rawData">The </param>
        /// <param name="cancellation">A token to cancel the async operation</param>
        /// <returns>A task that complets when the object data has been written to the data buffer</returns>
        public static Task GetAsync(this IGlobalCacheProvider cache, string key, IObjectData rawData, CancellationToken cancellation)
        {
            return cache.GetAsync(key, static (cd, data) => cd.SetData(data), rawData, cancellation);
        }

        /// <summary>
        /// Asynchronously sets (or updates) a cached value in the backing cache store 
        /// from the supplied raw data
        /// </summary>
        /// <param name="cache"></param>
        /// <param name="key">The key identifying the object to recover from cache</param>
        /// <param name="newKey">An optional key that will be changed for the new object</param>
        /// <param name="cancellation">A token to cancel the async operation</param>
        /// <param name="rawData">The raw data to store at the given key</param>
        /// <returns>A task that completes when the update operation has compelted</returns>
        public static Task AddOrUpdateAsync(this IGlobalCacheProvider cache, string key, string? newKey, IObjectData rawData, CancellationToken cancellation)
        {
            return cache.AddOrUpdateAsync(key, newKey, static cd => cd.GetData(), rawData, cancellation);
        }

        /// <summary>
        /// Asynchronously sets (or updates) a cached value in the backing cache store 
        /// from the supplied raw data
        /// </summary>
        /// <param name="cache"></param>
        /// <param name="key">The key identifying the object to recover from cache</param>
        /// <param name="newKey">An optional key that will be changed for the new object</param>
        /// <param name="cancellation">A token to cancel the async operation</param>
        /// <param name="rawData">The raw data to store at the given key</param>
        /// <returns>A task that completes when the update operation has compelted</returns>
        public static Task AddOrUpdateAsync(this IGlobalCacheProvider cache, string key, string? newKey, ReadOnlyMemory<byte> rawData, CancellationToken cancellation)
        {
            return cache.AddOrUpdateAsync(key, newKey, static cd => cd.Span, rawData, cancellation);
        }
    }
}