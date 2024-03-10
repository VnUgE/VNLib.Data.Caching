/*
* Copyright (c) 2024 Vaughn Nugent
* 
* Library: VNLib
* Package: VNLib.Data.Caching.ObjectCache
* File: BlobCacheTable.cs 
*
* BlobCacheTable.cs is part of VNLib.Data.Caching.ObjectCache which is part of the larger 
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
using System.Linq;
using System.Collections;
using System.Collections.Generic;

using VNLib.Utils;

namespace VNLib.Data.Caching.ObjectCache
{

    /// <summary>
    /// A concrete implementation of a <see cref="IBlobCacheTable"/>
    /// </summary>
    public sealed class BlobCacheTable : VnDisposeable, IBlobCacheTable
    {
        private readonly uint _tableSize;
        private readonly BlobCacheBucket[] _buckets;
        private readonly IPersistantCacheStore? _persistant;


        /// <summary>
        /// Initializes a new <see cref="BlobCacheTable"/>
        /// </summary>
        /// <param name="bucketSize">The number of elements in each bucket</param>
        /// <param name="tableSize">The number of buckets within the table</param>
        /// <param name="manager">A single cache memory manger to share across all buckets</param>
        /// <param name="persistantCache">An optional <see cref="IPersistantCacheStore"/> for persistant cache implementations</param>
        /// <exception cref="ArgumentNullException"></exception>
        /// <exception cref="ArgumentException"></exception>
        public BlobCacheTable(uint tableSize, uint bucketSize, ICacheEntryMemoryManager manager, IPersistantCacheStore? persistantCache)
            :this(tableSize, bucketSize, new SharedMemManager(manager), persistantCache)
        { }

        /// <summary>
        /// Initializes a new <see cref="BlobCacheTable"/>
        /// </summary>
        /// <param name="bucketSize">The number of elements in each bucket</param>
        /// <param name="tableSize">The number of buckets within the table</param>
        /// <param name="factory">A factory that can generate bucket-local memory managers</param>
        /// <param name="persistantCache">An optional <see cref="IPersistantCacheStore"/> for persistant cache implementations</param>
        /// <exception cref="ArgumentNullException"></exception>
        /// <exception cref="ArgumentException"></exception>
        public BlobCacheTable(uint tableSize, uint bucketSize, ICacheMemoryManagerFactory factory, IPersistantCacheStore? persistantCache)
        {
            ArgumentNullException.ThrowIfNull(factory);
            ArgumentOutOfRangeException.ThrowIfZero(tableSize);

            //Init bucket table
            _tableSize = tableSize;
            _persistant = persistantCache;

            //Init buckets
            InitBuckets(tableSize, bucketSize, out _buckets, factory, persistantCache);
        }


        private static void InitBuckets(uint size, uint bucketSize, out BlobCacheBucket[] table, ICacheMemoryManagerFactory man, IPersistantCacheStore? persistantCache)
        {
            table = new BlobCacheBucket[size];

            for(uint i = 0; i < size; i++)
            {
                //Get the memory manager for the bucket
                ICacheEntryMemoryManager manager = man.CreateForBucket(i);

                table[i] = new BlobCacheBucket(i, (int)bucketSize, manager, persistantCache);
            }
        }

        /*
         * A very simple algorithm that captures unique values
         * from an object id and builds an unsigned 32bit integer
         * used to determine the bucked index within the table.
         * 
         * This method will alawys result in the same index for 
         * for a given object-id
         */

        private uint FastGetBucketIndexFromId(ReadOnlySpan<char> objectId)
        {
            ArgumentOutOfRangeException.ThrowIfLessThan(objectId.Length, 4, nameof(objectId));

            Span<byte> buffer = stackalloc byte[4];

            //cast the characters 
            buffer[0] = (byte)objectId[0];
            buffer[1] = (byte)objectId[objectId.Length / 2];
            buffer[2] = (byte)objectId[1];
            buffer[3] = (byte)objectId[^1];

            //Read the buffer back to a uint and mod by the table size to get the bucket index
            return BitConverter.ToUInt32(buffer) % _tableSize;
        }
     

        ///<inheritdoc/>
        ///<exception cref="ObjectDisposedException"></exception>
        public IBlobCacheBucket GetBucket(ReadOnlySpan<char> objectId)
        {
            Check();

            //If tablesize is 1, skip lookup, otherwise perform bucket index lookup
            uint index = _tableSize == 1 ? 0 : FastGetBucketIndexFromId(objectId);

            return _buckets[index];
        }

        ///<inheritdoc/>
        protected sealed override void Free()
        {
            //Dispose persistance store
            using (_persistant)
            {
                //Dispose buckets
                Array.ForEach(_buckets, static b => b.Dispose());
            }
        }

        ///<inheritdoc/>
        public IEnumerator<IBlobCacheBucket> GetEnumerator()
        {
            Check();
            return _buckets.AsEnumerable().GetEnumerator();
        }

        ///<inheritdoc/>
        IEnumerator IEnumerable.GetEnumerator()
        {
            Check();
            return _buckets.AsEnumerable().GetEnumerator();
        }

        private sealed record class SharedMemManager(ICacheEntryMemoryManager Manager) : ICacheMemoryManagerFactory
        {
            ///<inheritdoc/>
            public ICacheEntryMemoryManager CreateForBucket(uint bucketId) => Manager;
          
        }
    }
}
