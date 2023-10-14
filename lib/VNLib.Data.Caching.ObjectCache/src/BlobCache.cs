/*
* Copyright (c) 2023 Vaughn Nugent
* 
* Library: VNLib
* Package: VNLib.Data.Caching.ObjectCache
* File: BlobCache.cs 
*
* BlobCache.cs is part of VNLib.Data.Caching.ObjectCache which is part of the larger 
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
using System.Diagnostics;
using System.Collections.Generic;

using VNLib.Utils.Memory.Caching;

namespace VNLib.Data.Caching.ObjectCache
{

    /// <summary>
    /// A general purpose binary data storage
    /// </summary>
    public sealed class BlobCache : LRUCache<string, CacheEntry>, IBlobCache, IMemoryCacheEntryFactory
    {
        private bool disposedValue;
        private IPersistantCacheStore? _persistance;

        ///<inheritdoc/>
        public override bool IsReadOnly { get; }

        ///<inheritdoc/>
        protected override int MaxCapacity { get; }
        
        ///<inheritdoc/>
        public ICacheEntryMemoryManager MemoryManager { get; }

        ///<inheritdoc/>
        public uint BucketId { get; }

        /// <summary>
        /// Initializes a new <see cref="BlobCache"/> store
        /// </summary>
        /// <param name="bucketId">The id of the bucket that manages this instance</param>
        /// <param name="maxCapacity">The maximum number of items to keep in memory</param>
        /// <param name="manager">The cache entry memory manager instance</param>
        /// <param name="store">The optional backing persistant cache storage</param>
        /// <exception cref="ArgumentException"></exception>
        public BlobCache(uint bucketId, int maxCapacity, ICacheEntryMemoryManager manager, IPersistantCacheStore? store)
            :base(maxCapacity, StringComparer.Ordinal)
        {
            if(maxCapacity < 1)
            {
                throw new ArgumentException("The maxium capacity of the store must be a positive integer larger than 0", nameof(maxCapacity));   
            }

            BucketId = bucketId;

            _persistance = store;

            MemoryManager = manager ?? throw new ArgumentNullException(nameof(manager));

            MaxCapacity = maxCapacity;

            //Update the lookup table size
            LookupTable.EnsureCapacity(maxCapacity);
        }

        ///<inheritdoc/>
        protected override bool CacheMiss(string key, out CacheEntry value)
        {
            if(_persistance == null)
            {
                value = default;
                return false;
            }
            //Use the persistant cache
            return _persistance.OnCacheMiss(BucketId, key, this, out value);
        }

        ///<inheritdoc/>
        protected override void Evicted(ref KeyValuePair<string, CacheEntry> evicted)
        {
            try
            {
                //Call persistance store record eviction
                _persistance?.OnEntryEvicted(BucketId, evicted.Key, evicted.Value);
            }
            finally
            {
                //Dispose the cache item
                evicted.Value.Dispose();
            }
        }

        ///<inheritdoc/>
        public bool TryChangeKey(string objectId, string newId, out CacheEntry entry)
        {
            //Try to get the node at the current key
            if (LookupTable.Remove(objectId, out LinkedListNode<KeyValuePair<string, CacheEntry>> ? node))
            {
                //Remove the node from the ll
                List.Remove(node);

                //Get the stored blob
                entry = node.ValueRef.Value;

                //Update the node vaule with the new key and the same entry
                node.Value = new KeyValuePair<string, CacheEntry>(newId, entry);

                //Add to end of list
                List.AddLast(node);

                //Re-add to lookup table with new key
                LookupTable.Add(newId, node);

                return true;
            }

            entry = default;
            return false;
        }

        ///<inheritdoc/>
        public override bool Remove(string key)
        {
            //Remove from persistant store also
            _persistance?.OnEntryDeleted(BucketId, key);

            //Remove the item from the lookup table and if it exists, remove the node from the list
            if (!LookupTable.Remove(key, out LinkedListNode<KeyValuePair<string, CacheEntry>>? node))
            {
                return false;
            }

            //always dispose blob
            using (node.ValueRef.Value)
            {
                //Remove the node from the list
                List.Remove(node);
            }

            return true;
        }

        /// <summary>
        /// Removes all cache entires and disposes their held resources
        /// </summary>
        public override void Clear()
        {
            //Start from first node
            LinkedListNode<KeyValuePair<string, CacheEntry>>? node = List.First;

            //Classic ll node itteration
            while(node != null)
            {
                //Dispose the cache entry
                node.ValueRef.Value.Dispose();
                
                //Move to next node
                node = node.Next;
            }
           
            //empty all cache entires in the store
            base.Clear();
        }

        ///<inheritdoc/>
        public bool Remove(string objectId, out CacheEntry entry)
        {
            //Try to get the stored object
            if(TryGetValue(objectId, out entry))
            {
                //remove the entry and bypass the disposal
                bool result = base.Remove(objectId);

                Debug.Assert(result == true, "The cache entry was found in the table, but failed to remove");

                return true;
            }
            
            entry = default;
            return false;
        }

        ///<inheritdoc/>
        void Dispose(bool disposing)
        {
            if (!disposedValue)
            {
                if (disposing)
                {
                    Clear();
                }
                disposedValue = true;
            }
        }

        ///<inheritdoc/>
        public void Dispose()
        {
            // Do not change this code. Put cleanup code in 'Dispose(bool disposing)' method
            Dispose(disposing: true);
            GC.SuppressFinalize(this);
        }


        ///<inheritdoc/>
        CacheEntry IMemoryCacheEntryFactory.CreateEntry(ReadOnlySpan<byte> entryData)
        {
            //Create entry from the internal heap
            return CacheEntry.Create(entryData, MemoryManager);
        }
    }
}
