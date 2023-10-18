/*
* Copyright (c) 2023 Vaughn Nugent
* 
* Library: VNLib
* Package: VNLib.Data.Caching.ObjectCache
* File: BlobCacheBucket.cs 
*
* BlobCacheBucket.cs is part of VNLib.Data.Caching.ObjectCache which is part of the larger 
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

using System.Threading;
using System.Threading.Tasks;

namespace VNLib.Data.Caching.ObjectCache
{

    /// <summary>
    /// A concrete implementation of an <see cref="IBlobCacheBucket"/>
    /// </summary>
    public sealed class BlobCacheBucket : IBlobCacheBucket
    {
        private readonly BlobCache _cacheTable;
        private readonly SemaphoreSlim _lock;

        ///<inheritdoc/>
        public uint Id { get; }

        /// <summary>
        /// Initialzies a new <see cref="BlobCacheBucket"/> and its underlying
        /// <see cref="IBlobCache"/>
        /// </summary>
        /// <param name="bucketCapacity">
        /// The maxium number of entries allowed in the LRU cache
        /// before LRU overflow happens.
        /// </param>
        /// <param name="bucketId">The unique id of the new bucket</param>
        /// <param name="memMan">The cache entry memory manager intance</param>
        /// <param name="persistantCache">An optional <see cref="IPersistantCacheStore"/> for cache persistance</param>
        public BlobCacheBucket(uint bucketId, int bucketCapacity, ICacheEntryMemoryManager memMan, IPersistantCacheStore? persistantCache)
        {
            Id = bucketId;
            _lock = new(1, 1);
            _cacheTable = new BlobCache(bucketId, bucketCapacity, memMan, persistantCache);
        }

        ///<inheritdoc/>
        public void Dispose()
        {
            _cacheTable.Dispose();
            _lock.Dispose();
        }

        ///<inheritdoc/>
        public async ValueTask<IBlobCache> ManualWaitAsync(CancellationToken cancellation)
        {
            await _lock.WaitAsync(cancellation).ConfigureAwait(false);
            return _cacheTable;
        }

        ///<inheritdoc/>
        public void Release()
        {
            _lock.Release();
        }
    }
}
