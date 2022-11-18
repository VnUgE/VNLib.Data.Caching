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
