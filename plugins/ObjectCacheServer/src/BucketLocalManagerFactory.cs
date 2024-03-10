/*
* Copyright (c) 2024 Vaughn Nugent
* 
* Library: VNLib
* Package: VNLib.Plugins.Extensions.VNCache
* File: BucketLocalManagerFactory.cs 
*
* BucketLocalManagerFactory.cs is part of VNLib.Plugins.Extensions.VNCache 
* which is part of the larger VNLib collection of libraries and utilities.
*
* VNLib.Plugins.Extensions.VNCache is free software: you can redistribute it and/or modify 
* it under the terms of the GNU Affero General Public License as 
* published by the Free Software Foundation, either version 3 of the
* License, or (at your option) any later version.
*
* VNLib.Plugins.Extensions.VNCache is distributed in the hope that it will be useful,
* but WITHOUT ANY WARRANTY; without even the implied warranty of
* MERCHANTABILITY or FITNESS FOR A PARTICULAR PURPOSE. See the
* GNU Affero General Public License for more details.
*
* You should have received a copy of the GNU Affero General Public License
* along with this program.  If not, see https://www.gnu.org/licenses/.
*/


using System;
using System.Linq;
using System.Buffers;
using System.Text.Json;
using System.Collections.Generic;
using System.Runtime.CompilerServices;

using VNLib.Plugins;
using VNLib.Utils;
using VNLib.Utils.Memory;
using VNLib.Utils.Memory.Diagnostics;
using VNLib.Utils.Logging;
using VNLib.Utils.Extensions;
using VNLib.Plugins.Extensions.Loading;
/*
 * How bucket local memory works:
 * 
 * The object cache library allows us to create a memory manager for use with 
 * an individual bucket. Each bucket is mutually exclusive, so memory operations 
 * are "thread-safe" This means we can create a heap for each bucket and use it
 * for all memory operations for that bucket. Since no contention will occur,
 * we can get a little more performance out of the memory operations. The internal
 * heap impls will still likely have locks enabled, but no contention will occur
 */

namespace VNLib.Data.Caching.ObjectCache.Server
{
    [ConfigurationName("memory_manager", Required = false)]
    internal sealed class BucketLocalManagerFactory : VnDisposeable, ICacheMemoryManagerFactory
    {
        private readonly LinkedList<BucketLocalManager> _managers = new ();
        private readonly bool _zeroAll;
        private readonly bool _enableHeapTracking;
        private readonly ILogProvider _statsLogger;

        ///<inheritdoc/>
        public ICacheEntryMemoryManager CreateForBucket(uint bucketId)
        {
            //Init a new heap for the individual bucket
            IUnmangedHeap localHeap = MemoryUtil.InitializeNewHeapForProcess();

            if (_enableHeapTracking)
            {
#pragma warning disable CA2000 // Dispose objects before losing scope
                localHeap = new TrackedHeapWrapper(localHeap, true);
#pragma warning restore CA2000 // Dispose objects before losing scope
            }

            BucketLocalManager manager = new (localHeap, bucketId, _zeroAll);
            _managers.AddLast(manager);

            return manager;
        }

        public BucketLocalManagerFactory(PluginBase plugin) : this(plugin, null)
        { }

        public BucketLocalManagerFactory(PluginBase plugin, IConfigScope? config)
        {
            if (config != null)
            {
                //Try to get the zero all flag
                _zeroAll = config.TryGetValue("zero_all", out JsonElement zeroEl) && zeroEl.GetBoolean();

                //Get the heap tracking flag
                _enableHeapTracking = config.TryGetValue("diag_mem", out JsonElement trackEl) && trackEl.GetBoolean();
            }

            _statsLogger = plugin.Log.CreateScope("Cache MemStats");
        }

        protected override void Free()
        {
            //Free heaps on exit
            foreach (BucketLocalManager manager in _managers)
            {
                manager.Heap.Dispose();
            }
        }

        public void LogHeapStats()
        {
            //If tracking is not enabled, the heap instances stored by the managers will not be tracked, and the cast in the code below will fail
            if (!_enableHeapTracking)
            {
                return;
            }

            string[] statsPerHeap = _managers.Select(hm =>
            {
                HeapStatistics stats = (hm.Heap as TrackedHeapWrapper)!.GetCurrentStats();
                return $"\tBucket {hm.BucketId}: Current {stats.AllocatedBytes / 1024}kB, Blocks {stats.AllocatedBlocks}, Max size {stats.MaxHeapSize / 1024}kB";

            }).ToArray();

            _statsLogger.Debug("Memory statistics for cache memory manager: {hm}\n{stats}", GetHashCode(), statsPerHeap);
        }

        /*
         * Buckets are mutually exclusive, so we can use a single heap for each bucket
         * to get a little more performance on memory operations
         */

        private sealed record class BucketLocalManager(IUnmangedHeap Heap, uint BucketId, bool Zero) : ICacheEntryMemoryManager
        {

            ///<inheritdoc/>
            public object AllocHandle(uint size) => Heap.Alloc<byte>(size, Zero);

            ///<inheritdoc/>
            public void FreeHandle(object handle)
            {
                ArgumentNullException.ThrowIfNull(handle);
                MemoryHandle<byte> _handle = Unsafe.As<object, MemoryHandle<byte>>(ref handle);

                //Free the handle
                _handle.Dispose();
            }

            ///<inheritdoc/>
            public uint GetHandleSize(object handle)
            {
                ArgumentNullException.ThrowIfNull(handle);
                MemoryHandle<byte> _handle = Unsafe.As<object, MemoryHandle<byte>>(ref handle);

                return (uint)_handle.Length;
            }

            ///<inheritdoc/>
            public Span<byte> GetSpan(object handle, uint offset, uint length)
            {
                ArgumentNullException.ThrowIfNull(handle);
                MemoryHandle<byte> _handle = Unsafe.As<object, MemoryHandle<byte>>(ref handle);

                return _handle.GetOffsetSpan(offset, checked((int)length));
            }

            ///<inheritdoc/>
            public MemoryHandle PinHandle(object handle, int offset)
            {
                ArgumentNullException.ThrowIfNull(handle);
                MemoryHandle<byte> _handle = Unsafe.As<object, MemoryHandle<byte>>(ref handle);

                //Pin the handle
                return _handle.Pin(offset);
            }

            ///<inheritdoc/>
            public void ResizeHandle(object handle, uint newSize)
            {
                ArgumentNullException.ThrowIfNull(handle);
                MemoryHandle<byte> _handle = Unsafe.As<object, MemoryHandle<byte>>(ref handle);

                //Resize the handle
                _handle.ResizeIfSmaller(newSize);
            }
        }
    }
}
