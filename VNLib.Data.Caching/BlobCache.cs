/*
* Copyright (c) 2022 Vaughn Nugent
* 
* Library: VNLib
* Package: VNLib.Data.Caching
* File: BlobCache.cs 
*
* BlobCache.cs is part of VNLib.Data.Caching which is part of the larger 
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
using System.IO;
using System.Linq;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;

using VNLib.Utils.IO;
using VNLib.Utils.Logging;
using VNLib.Utils.Memory;
using VNLib.Utils.Memory.Caching;


#nullable enable

namespace VNLib.Data.Caching
{
    /// <summary>
    /// A general purpose binary data storage
    /// </summary>
    public class BlobCache : LRUCache<string, MemoryHandle<byte>>
    {
        readonly IUnmangedHeap Heap;
        readonly DirectoryInfo SwapDir;
        readonly ILogProvider Log;
        ///<inheritdoc/>
        public override bool IsReadOnly { get; }
        ///<inheritdoc/>
        protected override int MaxCapacity { get; }
       
          
        /// <summary>
        /// Initializes a new <see cref="BlobCache"/> store
        /// </summary>
        /// <param name="swapDir">The <see cref="IsolatedStorageDirectory"/> to swap blob data to when cache</param>
        /// <param name="maxCapacity">The maximum number of items to keep in memory</param>
        /// <param name="log">A <see cref="ILogProvider"/> to write log data to</param>
        /// <param name="heap">A <see cref="IUnmangedHeap"/> to allocate buffers and store <see cref="BlobItem"/> data in memory</param>
        public BlobCache(DirectoryInfo swapDir, int maxCapacity, ILogProvider log, IUnmangedHeap heap)
            :base(StringComparer.Ordinal)
        {
            IsReadOnly = false;
            MaxCapacity = maxCapacity;
            SwapDir = swapDir;
            //Update the lookup table size
            LookupTable.EnsureCapacity(maxCapacity);
            //Set default heap if not specified
            Heap = heap;
            Log = log;
        }
        ///<inheritdoc/>
        protected override bool CacheMiss(string key, [NotNullWhen(true)] out MemoryHandle<byte>? value)
        {
            value = null;
            return false;
        }
        ///<inheritdoc/>
        protected override void Evicted(KeyValuePair<string, MemoryHandle<byte>> evicted)
        {
            //Dispose the blob
            evicted.Value.Dispose();
        }
        /// <summary>
        /// If the <see cref="BlobItem"/> is found in the store, changes the key 
        /// that referrences the blob. 
        /// </summary>
        /// <param name="currentKey">The key that currently referrences the blob in the store</param>
        /// <param name="newKey">The new key that will referrence the blob</param>
        /// <param name="blob">The <see cref="BlobItem"/> if its found in the store</param>
        /// <returns>True if the record was found and the key was changes</returns>
        public bool TryChangeKey(string currentKey, string newKey, [NotNullWhen(true)] out MemoryHandle<byte>? blob)
        {
            if (LookupTable.Remove(currentKey, out LinkedListNode<KeyValuePair<string, MemoryHandle<byte>>>? node))
            {
                //Remove the node from the ll
                List.Remove(node);
                //Update the node kvp
                blob = node.Value.Value;
                node.Value = new KeyValuePair<string, MemoryHandle<byte>>(newKey, blob);
                //Add to end of list
                List.AddLast(node);
                //Re-add to lookup table with new key
                LookupTable.Add(newKey, node);
                return true;
            }
            blob = null;
            return false;
        }
        /// <summary>
        /// Removes the <see cref="BlobItem"/> from the store without disposing the blobl
        /// </summary>
        /// <param name="key">The key that referrences the <see cref="BlobItem"/> in the store</param>
        /// <returns>A value indicating if the blob was removed</returns>
        public override bool Remove(string key)
        {
            //Remove the item from the lookup table and if it exists, remove the node from the list
            if (LookupTable.Remove(key, out LinkedListNode<KeyValuePair<string, MemoryHandle<byte>>>? node))
            {
                //Remove the new from the list
                List.Remove(node);
                //dispose the buffer
                node.Value.Value.Dispose();
                return true;
            }
            return false;
        }
        /// <summary>
        /// Removes and disposes all blobl elements in cache (or in the backing store)
        /// </summary>
        public override void Clear()
        {
            foreach (MemoryHandle<byte> blob in List.Select(kp => kp.Value))
            {
                blob.Dispose();
            }
            base.Clear();
        }
    }
}
