/*
* Copyright (c) 2022 Vaughn Nugent
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
using System.Collections.Generic;

using VNLib.Utils.Memory.Caching;

namespace VNLib.Data.Caching
{

    /// <summary>
    /// A general purpose binary data storage
    /// </summary>
    public class BlobCache : LRUCache<string, CacheEntry>
    {
        ///<inheritdoc/>
        public override bool IsReadOnly { get; }

        ///<inheritdoc/>
        protected override int MaxCapacity { get; }


        /// <summary>
        /// Initializes a new <see cref="BlobCache"/> store
        /// </summary>
        /// <param name="maxCapacity">The maximum number of items to keep in memory</param>
        /// <exception cref="ArgumentException"></exception>
        public BlobCache(int maxCapacity)
            :base(StringComparer.Ordinal)
        {
            if(maxCapacity < 1)
            {
                throw new ArgumentException("The maxium capacity of the store must be a positive integer larger than 0", nameof(maxCapacity));   
            }

            MaxCapacity = maxCapacity;

            //Update the lookup table size
            LookupTable.EnsureCapacity(maxCapacity);
        }

        ///<inheritdoc/>
        protected override bool CacheMiss(string key, out CacheEntry value)
        {
            value = default;
            return false;
        }

        ///<inheritdoc/>
        protected override void Evicted(ref KeyValuePair<string, CacheEntry> evicted)
        {
            //Dispose the cache item
            evicted.Value.Dispose();
        }

        /// <summary>
        /// If the <see cref="CacheEntry"/> is found in the store, changes the key 
        /// that referrences the blob. 
        /// </summary>
        /// <param name="currentKey">The key that currently referrences the blob in the store</param>
        /// <param name="newKey">The new key that will referrence the blob</param>
        /// <param name="blob">The <see cref="CacheEntry"/> if its found in the store</param>
        /// <returns>True if the record was found and the key was changes</returns>
        public bool TryChangeKey(string currentKey, string newKey, out CacheEntry blob)
        {
            //Try to get the node at the current key
            if (LookupTable.Remove(currentKey, out LinkedListNode<KeyValuePair<string, CacheEntry>> ? node))
            {
                //Remove the node from the ll
                List.Remove(node);

                //Get the stored blob
                blob = node.ValueRef.Value;

                //Update the 
                node.Value = new KeyValuePair<string, CacheEntry>(newKey, blob);

                //Add to end of list
                List.AddLast(node);

                //Re-add to lookup table with new key
                LookupTable.Add(newKey, node);

                return true;
            }

            blob = default;
            return false;
        }

        /// <summary>
        /// Removes the <see cref="CacheEntry"/> from the store, and frees its resources
        /// </summary>
        /// <param name="key">The key that referrences the <see cref="CacheEntry"/> in the store</param>
        /// <returns>A value indicating if the blob was removed</returns>
        public override bool Remove(string key)
        {
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
    }
}
