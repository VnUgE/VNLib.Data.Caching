/*
* Copyright (c) 2023 Vaughn Nugent
* 
* Library: VNLib
* Package: VNLib.Data.Caching.ObjectCache
* File: IBlobCache.cs 
*
* IBlobCache.cs is part of VNLib.Data.Caching.ObjectCache which is part of the larger 
* VNLib collection of libraries and utilities.
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
using System.Collections.Generic;

using VNLib.Utils.Memory;

namespace VNLib.Data.Caching.ObjectCache
{
    /// <summary>
    /// Represents a binary data cache store
    /// </summary>
    public interface IBlobCache : IEnumerable<KeyValuePair<string, CacheEntry>>, IDisposable
    {
        /// <summary>
        /// The internal heap used to allocate <see cref="CacheEntry"/> buffers
        /// </summary>
        IUnmangedHeap CacheHeap { get; }

        /// <summary>
        /// Attempts to retreive the entry at the given id.
        /// </summary>
        /// <param name="objectId">The id of the object to locate</param>
        /// <param name="entry">The cache entry if found, default otherwise</param>
        /// <returns>True if the entry was assigned</returns>
        bool TryGetValue(string objectId, out CacheEntry entry);

        /// <summary>
        /// Attempts to relocate the entry in the table by its new id.
        /// </summary>
        /// <param name="objectId">The original id of the entry to modify</param>
        /// <param name="newId">The new id of the entry</param>
        /// <param name="entry">The original entry if found, default otherwise</param>
        /// <returns>True if the item was located and successfully updated, false if the operation failed</returns>
        bool TryChangeKey(string objectId, string newId, out CacheEntry entry);

        /// <summary>
        /// Adds the entry to the table by the id
        /// </summary>
        /// <param name="objectId"></param>
        /// <param name="entry">The entry to store in the table</param>
        void Add(string objectId, CacheEntry entry);

        /// <summary>
        /// Attempts to remove the entry at the given id, and returns the 
        /// entry if located.
        /// </summary>
        /// <param name="objectId">The id of the entry to remove</param>
        /// <param name="entry">The entry if found, default otherwise</param>
        /// <returns>True if the entry existed in the store, false otherwise</returns>
        /// <remarks>
        /// NOTE: If the return value is true, the store no longer maintains the lifetime
        /// of the returned <see cref="CacheEntry"/>. You must manually dispose the entry 
        /// to avoid memory leaks.
        /// </remarks>
        bool Remove(string objectId, out CacheEntry entry);

        /// <summary>
        /// Attempts to remove the entry at the given id, and release its memory.
        /// </summary>
        /// <param name="objectId">The id of the entry to remove</param>
        /// <returns>True if the entry was found and disposed</returns>
        bool Remove(string objectId);
    }
}
