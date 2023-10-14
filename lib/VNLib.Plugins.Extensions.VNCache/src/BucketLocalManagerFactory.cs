/*
* Copyright (c) 2023 Vaughn Nugent
* 
* Library: VNLib
* Package: ObjectCacheServer
* File: BucketLocalManagerFactory.cs 
*
* BucketLocalManagerFactory.cs is part of ObjectCacheServer which is part of the larger 
* VNLib collection of libraries and utilities.
*
* ObjectCacheServer is free software: you can redistribute it and/or modify 
* it under the terms of the GNU Affero General Public License as 
* published by the Free Software Foundation, either version 3 of the
* License, or (at your option) any later version.
*
* ObjectCacheServer is distributed in the hope that it will be useful,
* but WITHOUT ANY WARRANTY; without even the implied warranty of
* MERCHANTABILITY or FITNESS FOR A PARTICULAR PURPOSE. See the
* GNU Affero General Public License for more details.
*
* You should have received a copy of the GNU Affero General Public License
* along with this program.  If not, see https://www.gnu.org/licenses/.
*/

using System;
using System.Buffers;
using System.Text.Json;
using System.Collections.Generic;
using System.Runtime.CompilerServices;

using VNLib.Plugins;
using VNLib.Utils;
using VNLib.Utils.Memory;
using VNLib.Utils.Extensions;
using VNLib.Plugins.Extensions.Loading;

namespace VNLib.Data.Caching.ObjectCache.Server
{
    [ConfigurationName("memory_manager", Required = false)]
    internal sealed class BucketLocalManagerFactory : VnDisposeable, ICacheMemoryManagerFactory
    {
        private readonly LinkedList<BucketLocalManager> _managers = new ();
        private readonly bool _zeroAll;

        ///<inheritdoc/>
        public ICacheEntryMemoryManager CreateForBucket(uint bucketId)
        {
            //Init a new heap for a individual bucket
            IUnmangedHeap localHeap = MemoryUtil.InitializeNewHeapForProcess();

            BucketLocalManager manager = new (localHeap, bucketId, _zeroAll);
            _managers.AddLast(manager);

            return manager;
        }

        /// <summary>
        /// Creates a new <see cref="BucketLocalManagerFactory"/> with the specified zero all flag 
        /// that is not managed by a plugin instance
        /// </summary>
        /// <param name="zeroAll">Forces all allocations to be zeroed before being returned to callers</param>
        /// <returns></returns>
        public static BucketLocalManagerFactory Create(bool zeroAll) => new (zeroAll);

        private BucketLocalManagerFactory(bool zeroAll)
        { 
            _zeroAll = zeroAll;
        }

        public BucketLocalManagerFactory(PluginBase plugin) : this(plugin, null)
        { }

        public BucketLocalManagerFactory(PluginBase plugin, IConfigScope? config)
        {
            if (config != null)
            {
                //Try to get the zero all flag
                if (config.TryGetValue("zero_all", out JsonElement zeroEl))
                {
                    _zeroAll = zeroEl.GetBoolean();
                }
            }
        }

        protected override void Free()
        {
            //Free heaps on exit
            foreach (BucketLocalManager manager in _managers)
            {
                manager.Heap.Dispose();
            }
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
                _ = handle ?? throw new ArgumentNullException(nameof(handle));
                MemoryHandle<byte> _handle = Unsafe.As<object, MemoryHandle<byte>>(ref handle);

                //Free the handle
                _handle.Dispose();
            }

            ///<inheritdoc/>
            public uint GetHandleSize(object handle)
            {
                _ = handle ?? throw new ArgumentNullException(nameof(handle));
                MemoryHandle<byte> _handle = Unsafe.As<object, MemoryHandle<byte>>(ref handle);

                return (uint)_handle.Length;
            }

            ///<inheritdoc/>
            public Span<byte> GetSpan(object handle, uint offset, uint length)
            {
                _ = handle ?? throw new ArgumentNullException(nameof(handle));
                MemoryHandle<byte> _handle = Unsafe.As<object, MemoryHandle<byte>>(ref handle);

                return _handle.GetOffsetSpan(offset, checked((int)length));
            }

            ///<inheritdoc/>
            public MemoryHandle PinHandle(object handle, int offset)
            {
                _ = handle ?? throw new ArgumentNullException(nameof(handle));
                MemoryHandle<byte> _handle = Unsafe.As<object, MemoryHandle<byte>>(ref handle);

                //Pin the handle
                return _handle.Pin(offset);
            }

            ///<inheritdoc/>
            public void ResizeHandle(object handle, uint newSize)
            {
                _ = handle ?? throw new ArgumentNullException(nameof(handle));
                MemoryHandle<byte> _handle = Unsafe.As<object, MemoryHandle<byte>>(ref handle);

                //Resize the handle
                _handle.ResizeIfSmaller(newSize);
            }
        }
    }
}
