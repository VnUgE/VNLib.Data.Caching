/*
* Copyright (c) 2024 Vaughn Nugent
* 
* Library: VNLib
* Package: VNLib.Data.Caching.Providers.VNCache
* File: AddOrUpdateBuffer.cs 
*
* AddOrUpdateBuffer.cs is part of VNLib.Data.Caching.Providers.VNCache 
* which is part of the larger VNLib collection of libraries and utilities.
*
* VNLib.Data.Caching.Providers.VNCache is free software: you can redistribute it and/or modify 
* it under the terms of the GNU Affero General Public License as 
* published by the Free Software Foundation, either version 3 of the
* License, or (at your option) any later version.
*
* VNLib.Data.Caching.Providers.VNCache is distributed in the hope that it will be useful,
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

namespace VNLib.Data.Caching.Providers.VNCache.Internal
{
    /// <summary>
    /// Implements a buffer writer that serves to serialize object data and 
    /// store the object data for use by the memory cache store, and the 
    /// remote cache store
    /// </summary>
    internal sealed class AddOrUpdateBuffer(IUnmangedHeap heap) : VnDisposeable, IBufferWriter<byte>, IObjectData
    {
        private int _count;
        private MemoryHandle<byte>? _buffer;

        ///<inheritdoc/>
        public void Advance(int count)
        {
            //Update count
            _count += count;
        }

        ///<inheritdoc/>
        Memory<byte> IBufferWriter<byte>.GetMemory(int sizeHint)
        {
            throw new NotImplementedException();
        }

        ///<inheritdoc/>
        Span<byte> IBufferWriter<byte>.GetSpan(int sizeHint)
        {
            //Round to nearest page for new size
            nint newSize = MemoryUtil.NearestPage(sizeHint + _count);

            //Alloc buffer it not yet allocated
            if (_buffer == null)
            {
                _buffer = MemoryUtil.SafeAlloc<byte>(heap, newSize, false);
            }
            else
            {
                //check for resize if already allocated
                _buffer.ResizeIfSmaller(newSize);
            }

            return _buffer.AsSpan(_count);
        }

        void IObjectData.SetData(ReadOnlySpan<byte> data)
        {
            throw new NotSupportedException();
        }

        ///<inheritdoc/>
        public ReadOnlySpan<byte> GetData() => _buffer!.AsSpan(0, _count);

        protected override void Free()
        {
            _buffer?.Dispose();
        }
    }
}