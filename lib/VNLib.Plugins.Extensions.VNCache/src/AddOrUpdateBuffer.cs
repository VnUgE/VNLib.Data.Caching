/*
* Copyright (c) 2023 Vaughn Nugent
* 
* Library: VNLib
* Package: VNLib.Plugins.Extensions.VNCache
* File: AddOrUpdateBuffer.cs 
*
* AddOrUpdateBuffer.cs is part of VNLib.Plugins.Extensions.VNCache 
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
using System.Buffers;

using VNLib.Utils;
using VNLib.Utils.Memory;
using VNLib.Utils.Extensions;
using VNLib.Data.Caching;

namespace VNLib.Plugins.Extensions.VNCache
{
    /// <summary>
    /// Implements a buffer writer that serves to serialize object data and 
    /// store the object data for use by the memory cache store, and the 
    /// remote cache store
    /// </summary>
    class AddOrUpdateBuffer : VnDisposeable, IBufferWriter<byte>, IObjectData
    {
        private int _count;
        private readonly IUnmangedHeap _heap;
        private MemoryHandle<byte>? _buffer;

        public AddOrUpdateBuffer(IUnmangedHeap heap)
        {
            _heap = heap;
        }

        public void Advance(int count)
        {
            //Update count
            _count += count;
        }

        public Memory<byte> GetMemory(int sizeHint = 0)
        {
            throw new NotImplementedException();
        }

        public Span<byte> GetSpan(int sizeHint = 0)
        {
            //Round to nearest page for new size
            nint newSize = MemoryUtil.NearestPage(sizeHint + _count);

            //Alloc buffer it not yet allocated
            if (_buffer == null)
            {
                _buffer = _heap.Alloc<byte>(newSize);
            }
            else
            {
                //check for resize if allocated
                _buffer.ResizeIfSmaller(newSize);
            }

            return _buffer.AsSpan(_count);
        }

        public void SetData(ReadOnlySpan<byte> data)
        {
            throw new NotSupportedException();
        }

        public ReadOnlySpan<byte> GetData()
        {
            //Get stored data from within handle
            return _buffer!.AsSpan(0, _count);
        }

        protected override void Free()
        {
            _buffer?.Dispose();
        }
    }
}