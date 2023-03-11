/*
* Copyright (c) 2023 Vaughn Nugent
* 
* Library: VNLib
* Package: VNLib.Data.Caching.ObjectCache
* File: IPersistantCacheStore.cs 
*
* IPersistantCacheStore.cs is part of VNLib.Data.Caching.ObjectCache which 
* is part of the larger VNLib collection of libraries and utilities.
*
* VNLib.Data.Caching.ObjectCache is free software: you can redistribute it and/or modify 
* it under the terms of the GNU Affero General Public License as 
* published by the Free Software Foundation, either version 3 of the
* License, or (at your option) any later version.
*
* VNLib.Data.Caching.ObjectCache is distributed in the hope that it will be useful,
* but WITHOUT ANY WARRANTY; without even the implied warranty of
* MERCHANTABILITY or FITNESS FOR A PARTICULAR PURPOSE. See the
* GNU Affero General Public License for more details.
*
* You should have received a copy of the GNU Affero General Public License
* along with this program.  If not, see https://www.gnu.org/licenses/.
*/

using System;

namespace VNLib.Data.Caching.ObjectCache
{
    /// <summary>
    /// Provides a persitance layer to memory caching.
    /// </summary>
    public interface IPersistantCacheStore : IDisposable
    {
        /// <summary>
        /// Invoked when an entry has been evicted from main-memory cache
        /// and is expected to be stored in a "persistant" storage solution.
        /// <para>
        /// When this method returns, the <paramref name="entry"/> is no longer valid.
        /// </para>
        /// <para>
        /// This method is called while the bucket lock is held. This call is maded
        /// during an <see cref="IBlobCache.Add(string, CacheEntry)"/> method call.
        /// </para>
        /// </summary>
        /// <param name="bucketId">The id of the bucket requesting the operation</param>
        /// <param name="key">The key identifying the the entry</param>
        /// <param name="entry">The entry containing the object data to store</param>
        void OnEntryEvicted(uint bucketId, string key, in CacheEntry entry);

        /// <summary>
        /// Called when a cache item does not exist in main memory cache and should
        /// be promoted from persistant cache to main memory cache.
        /// <para>
        /// This method is called while the bucket lock is held. This call is maded
        /// during an <see cref="IBlobCache.Add(string, CacheEntry)"/> method call.
        /// </para>
        /// <para>
        /// The <see cref="IMemoryCacheEntryFactory"/> should be used to create the 
        /// cache entry for the return value. Once this method returns, the caller owns the new <see cref="CacheEntry"/>
        /// </para>
        /// </summary>
        /// <param name="key">The key identifying the entry to promot</param>
        /// <param name="factory">The cache entry factory</param>
        /// <param name="bucketId">The id of the bucket requesting the operation</param>
        /// <param name="entry">The newly created entry when data is found</param>
        /// <returns>
        /// A value inidcating if the entry was successfully recovered from the persistant storage and 
        /// was successfully promoted.
        /// </returns>
        bool OnCacheMiss(uint bucketId, string key, IMemoryCacheEntryFactory factory, out CacheEntry entry);

        /// <summary>
        /// Removes an entry from the backing store
        /// </summary>
        /// <param name="key">The key identifying the entry to remove</param>
        /// <param name="bucketId">The id of the bucket requesting the operation</param>
        void OnEntryDeleted(uint bucketId, string key);
    }
}
