/*
* Copyright (c) 2022 Vaughn Nugent
* 
* Library: VNLib
* Package: VNLib.Data.Caching
* File: CacheListener.cs 
*
* CacheListener.cs is part of VNLib.Data.Caching which is part of the larger 
* VNLib collection of libraries and utilities.
*
* VNLib.Data.Caching is free software: you can redistribute it and/or modify 
* it under the terms of the GNU General Public License as published
* by the Free Software Foundation, either version 2 of the License,
* or (at your option) any later version.
*
* VNLib.Data.Caching is distributed in the hope that it will be useful,
* but WITHOUT ANY WARRANTY; without even the implied warranty of
* MERCHANTABILITY or FITNESS FOR A PARTICULAR PURPOSE. See the GNU 
* General Public License for more details.
*
* You should have received a copy of the GNU General Public License 
* along with VNLib.Data.Caching. If not, see http://www.gnu.org/licenses/.
*/

using System;
using System.IO;

using VNLib.Utils.Memory;
using VNLib.Net.Messaging.FBM.Server;

namespace VNLib.Data.Caching
{
    /// <summary>
    /// A base implementation of a memory/disk LRU data cache FBM listener
    /// </summary>
    public abstract class CacheListener : FBMListenerBase
    {
        /// <summary>
        /// The directory swap files will be stored
        /// </summary>
        public DirectoryInfo? Directory { get; private set; }
        /// <summary>
        /// The Cache store to access data blobs
        /// </summary>
        protected BlobCache? Cache { get; private set; }
        /// <summary>
        /// The <see cref="IUnmangedHeap"/> to allocate buffers from
        /// </summary>
        protected IUnmangedHeap? Heap { get; private set; }
      
        /// <summary>
        /// Initializes the <see cref="Cache"/> data store
        /// </summary>
        /// <param name="dir">The directory to swap cache records to</param>
        /// <param name="cacheSize">The size of the LRU cache</param>
        /// <param name="heap">The heap to allocate buffers from</param>
        protected void InitCache(DirectoryInfo dir, int cacheSize, IUnmangedHeap heap)
        {
            Heap = heap;
            Cache = new(dir, cacheSize, Log, Heap);
            Directory = dir;
        }
    }
}
