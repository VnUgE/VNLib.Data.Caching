/*
* Copyright (c) 2023 Vaughn Nugent
* 
* Library: VNLib
* Package: VNLib.Data.Caching.ObjectCache
* File: CacheEntry.cs 
*
* CacheEntry.cs is part of VNLib.Data.Caching.ObjectCache which is part of the larger 
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
using System.Buffers;
using System.Diagnostics;
using System.Buffers.Binary;
using System.Runtime.CompilerServices;

using VNLib.Utils.Memory;
using VNLib.Utils.Extensions;

namespace VNLib.Data.Caching
{
    /// <summary>
    /// A structure that represents an item in cache. It contains the binary content
    /// of a cache entry by its internal memory handle
    /// </summary>
    public readonly record struct CacheEntry : IDisposable
    {
        private const int TIME_SEGMENT_SIZE = sizeof(long);

        private const int LENGTH_SEGMENT_SIZE = sizeof(int);

        private const int DATA_SEGMENT_START = TIME_SEGMENT_SIZE + LENGTH_SEGMENT_SIZE;


        //Only contain ref to backing handle to keep struct size small
        private readonly MemoryHandle<byte> _handle;


        /// <summary>
        /// Creates a new <see cref="CacheEntry"/> and copies the initial data to the internal buffer
        /// </summary>
        /// <param name="data">The initial data to store</param>
        /// <param name="heap">The heap to allocate the buffer from</param>
        /// <returns>The newly initialized and ready to use <see cref="CacheEntry"/></returns>
        public static CacheEntry Create(ReadOnlySpan<byte> data, IUnmangedHeap heap)
        {
            //Calc buffer size
            int bufferSize = GetRequiredHandleSize(data.Length);

            //Alloc buffer
            MemoryHandle<byte> handle = heap.Alloc<byte>(bufferSize);
            
            //Create new entry from handle
            CacheEntry entry = new (handle);
            entry.SetLength(data.Length);

            //Get the data segment
            Span<byte> segment = entry.GetDataSegment();

            Debug.Assert(segment.Length == data.Length);

            //Copy data segment
            data.CopyTo(segment);

            return entry;
        }

       
        private static int GetRequiredHandleSize(int size)
        {
            //Caculate the minimum handle size to store all required information, rounded to nearest page
            return (int)MemoryUtil.NearestPage(size + DATA_SEGMENT_START);
        }

        private CacheEntry(MemoryHandle<byte> handle)
        {
            _handle = handle;
        }

        ///<inheritdoc/>
        public readonly void Dispose() => _handle?.Dispose();
       
        private readonly Span<byte> GetTimeSegment() => _handle.AsSpan(0, TIME_SEGMENT_SIZE);
      
        private readonly Span<byte> GetLengthSegment() => _handle.AsSpan(TIME_SEGMENT_SIZE, LENGTH_SEGMENT_SIZE);

        /// <summary>
        /// Gets the size of the block of memory held by the underlying handle
        /// </summary>
        /// <returns>The size of the block held by the current entry</returns>
        /// <exception cref="ObjectDisposedException"></exception>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public readonly nuint GetMemoryUsage()
        {
            _handle.ThrowIfClosed();
            return _handle.ByteLength;
        }

        /// <summary>
        /// Gets the last set time
        /// </summary>
        /// <returns>The last date stored</returns>
        /// <exception cref="ObjectDisposedException"></exception>
        public readonly DateTime GetTime()
        {
            //Get the time segment and write the value in big endian
            ReadOnlySpan<byte> segment = GetTimeSegment();

            long ticks = BinaryPrimitives.ReadInt64BigEndian(segment);

            //ticks back to 
            return new(ticks);
        }

        /// <summary>
        /// Sets the last modified time
        /// </summary>
        /// <param name="time">The new time to set the handle to</param>
        /// <exception cref="ObjectDisposedException"></exception>
        public readonly void SetTime(DateTime time)
        {
            //Get native ticks value
            long timeData = time.Ticks;

            //Get the time segment and write the value in big endian
            Span<byte> segment = GetTimeSegment();

            BinaryPrimitives.WriteInt64BigEndian(segment, timeData);
        }

        /// <summary>
        /// Gets the length of the data segment
        /// </summary>
        /// <returns>The length of the data segment</returns>
        /// <exception cref="ObjectDisposedException"></exception>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public readonly int GetLength()
        {
            //Get the length segment
            ReadOnlySpan<byte> segment = GetLengthSegment();
            //Recover the integer
            return BinaryPrimitives.ReadInt32BigEndian(segment);
        }

        private readonly void SetLength(int length)
        {
            //Get the length segment
            Span<byte> segment = GetLengthSegment();
        
            //Update the length value
            BinaryPrimitives.WriteInt32BigEndian(segment, length);
        }

        /// <summary>
        /// Gets the stored data segment
        /// </summary>
        /// <returns>The data segment</returns>
        /// <exception cref="ObjectDisposedException"></exception>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public readonly Span<byte> GetDataSegment()
        {
            //Get the actual length of the segment
            int length = GetLength();
            //Get the segment from its begining offset and 
            return _handle.AsSpan(DATA_SEGMENT_START, length);
        }

        /// <summary>
        /// Writes the specified segment to the internal buffer and resizes the buffer if necessary.
        /// This operation overwrites any previously stored data
        /// </summary>
        /// <param name="data">The data segment to store</param>
        /// <exception cref="ObjectDisposedException"></exception>
        public readonly void UpdateData(ReadOnlySpan<byte> data)
        {
            //Calc required buffer size
            int bufferSize = GetRequiredHandleSize(data.Length);

            //Resize handle if required
            _handle.ResizeIfSmaller(bufferSize);

            //Reset data length
            SetLength(data.Length);

            //Get the data segment
            Span<byte> segment = GetDataSegment();

            //Test segment length is equivalent to the requested data length
            Debug.Assert(segment.Length == data.Length);

            //Copy data segment
            data.CopyTo(segment);
        }

        ///<inheritdoc/>
        public override int GetHashCode() => _handle.GetHashCode();

        /// <summary>
        /// Gets a <see cref="MemoryHandle"/> offset to the start of the 
        /// internal data segment, and avoids calling the fixed keyword.
        /// The handle must be disposed/released to avoid memeory leaks.
        /// </summary>
        /// <remarks>
        /// WARNING: You must respect the <see cref="GetLength"/> return value so 
        /// as no to overrun the valid data segment.
        /// </remarks>
        /// <returns>A handle that points to the begining of the data segment</returns>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public readonly MemoryHandle UnsafeGetDataSegmentHandle()
        {
            //Get the handle offset to the data segment start, the caller must know when the data segment ends
            return _handle.Pin(DATA_SEGMENT_START);
        }
    }
}
