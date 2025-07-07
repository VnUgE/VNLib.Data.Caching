/*
* Copyright (c) 2025 Vaughn Nugent
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

        /// <summary>
        /// Creates a new <see cref="CacheEntry"/> and copies the initial data to the internal buffer
        /// </summary>
        /// <param name="data">The initial data to store</param>
        /// <param name="dataManager">The heap to allocate the buffer from</param>
        /// <returns>The newly initialized and ready to use <see cref="CacheEntry"/></returns>
        /// <exception cref="ArgumentNullException"></exception>
        public static CacheEntry Create(ReadOnlySpan<byte> data, ICacheEntryMemoryManager dataManager)
        {
            ArgumentNullException.ThrowIfNull(dataManager);            

            //Calc buffer size
            uint bufferSize = CalculateMinimumHandleSize(data.Length);

            object handle = dataManager.AllocHandle(bufferSize);

            //Create new entry from handle
            CacheEntry entry = new(dataManager, handle);
            entry.SetLength((uint)data.Length);

            //Get the data segment
            Span<byte> segment = entry.GetDataSegment();

            Debug.Assert(segment.Length == data.Length);

            //Copy data segment
            data.CopyTo(segment);

            return entry;
        }

        /// <summary>
        /// Creates a new <see cref="CacheEntry"/> from an existing handle
        /// </summary>
        /// <param name="handle">The cache data handle to create the entry around</param>
        /// <param name="manager">The cache entry memory manager the handle blongs to</param>
        /// <returns>The re-constructed entry</returns>
        /// <exception cref="ArgumentNullException"></exception>
        /// <exception cref="ArgumentException"></exception>
        public static CacheEntry FromExistingHandle(object handle, ICacheEntryMemoryManager manager)
        {
            ArgumentNullException.ThrowIfNull(handle);
            ArgumentNullException.ThrowIfNull(manager);

            //validate handle size it at least the minimum size
            if (manager.GetHandleSize(handle) < DATA_SEGMENT_START)
            {
                throw new ArgumentException("Memory segment is too small to be a valid cache entry");
            }

            return new(manager, handle);
        }

        /// <summary>
        /// Calculates the minimum size of the handle required to store the desired
        /// data segment size.
        /// </summary>
        /// <param name="size">The size of data (in bytes) desired to be stored in the entry</param>
        /// <returns>The minimum handle size required in bytes</returns>
        public static uint CalculateMinimumHandleSize(int size)
        {
            //Calculate the minimum handle size to store all required information
            return (uint)(size + DATA_SEGMENT_START);
        }

        private readonly ICacheEntryMemoryManager _manager;
        private readonly object _handle;

        private CacheEntry(ICacheEntryMemoryManager manager, object handle)
        {
            _manager = manager;
            _handle = handle;
        }

        ///<inheritdoc/>
        public readonly void Dispose() => _manager?.FreeHandle(_handle);

        private readonly Span<byte> GetTimeSegment()
            => _manager.GetSpan(_handle, 0, TIME_SEGMENT_SIZE);

        private readonly Span<byte> GetLengthSegment()
            => _manager.GetSpan(_handle, TIME_SEGMENT_SIZE, LENGTH_SEGMENT_SIZE);

        /// <summary>
        /// Gets the size of the block of memory held by the underlying handle
        /// </summary>
        /// <returns>The size of the block held by the current entry</returns>
        /// <exception cref="ObjectDisposedException"></exception>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public readonly nuint GetMemoryUsage() => _manager.GetHandleSize(_handle);

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
        public readonly uint GetLength()
        {
            //Get the length segment
            ReadOnlySpan<byte> segment = GetLengthSegment();
            //Recover the integer
            return BinaryPrimitives.ReadUInt32BigEndian(segment);
        }

        private readonly void SetLength(uint length)
        {
            //Get the length segment
            Span<byte> segment = GetLengthSegment();

            //Update the length value
            BinaryPrimitives.WriteUInt32BigEndian(segment, length);
        }

        /// <summary>
        /// Gets the stored data segment
        /// </summary>
        /// <returns>The data segment</returns>
        /// <exception cref="ObjectDisposedException"></exception>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public readonly Span<byte> GetDataSegment()
        {
            //Get the segment from its begining offset and 
            return _manager.GetSpan(_handle, DATA_SEGMENT_START, GetLength());
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
            uint bufferSize = CalculateMinimumHandleSize(data.Length);

            //Resize buffer if necessary
            if (_manager.GetHandleSize(_handle) < bufferSize)
            {
                //resize handle
                _manager.ResizeHandle(_handle, bufferSize);
            }

            //Reset data length
            SetLength((uint)data.Length);

            //Get the data segment
            Span<byte> segment = GetDataSegment();

            //Test segment length is equivalent to the requested data length
            Debug.Assert(segment.Length == data.Length);

            //Copy data segment
            data.CopyTo(segment);
        }

        /// <summary>
        /// Gets a <see cref="MemoryHandle"/> offset to the start of the 
        /// internal data segment, and avoids calling the fixed keyword.
        /// The handle must be disposed/released to avoid memeory leaks.
        /// </summary>
        /// <remarks>
        /// WARNING: You must respect the <see cref="GetLength"/> return value so 
        /// as not to overrun the valid data segment.
        /// </remarks>
        /// <returns>A handle that points to the begining of the data segment</returns>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public readonly MemoryHandle UnsafeGetDataSegmentHandle()
        {
            //Get the handle offset to the data segment start, the caller must know when the data segment ends
            return _manager.PinHandle(_handle, DATA_SEGMENT_START);
        }

        /// <summary>
        /// Gets the internal memory handle and manager its associated with
        /// </summary>
        /// <param name="handle">The opaque memory handle</param>
        /// <param name="manager">The associated memory manager</param>
        public readonly void GetInternalHandle(out object handle, out ICacheEntryMemoryManager manager)
        {
            handle = _handle;
            manager = _manager;
        }
    }
}
