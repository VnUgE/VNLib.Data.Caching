/*
* Copyright (c) 2023 Vaughn Nugent
* 
* Library: VNLib
* Package: VNLib.Data.Caching.ObjectCache
* File: ICacheEntryMemoryManager.cs 
*
* ICacheEntryMemoryManager.cs is part of VNLib.Data.Caching.ObjectCache which is part 
* of the larger VNLib collection of libraries and utilities.
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

namespace VNLib.Data.Caching
{
    /// <summary>
    /// Provides a way to allocate and manage opaque memory hanles for a cache entry data
    /// </summary>
    public interface ICacheEntryMemoryManager
    {
        /// <summary>
        /// Allocates a new handle of at-least the specified size or larger.
        /// </summary>
        /// <param name="size">The desired minimum size of the handle</param>
        /// <returns>A referrence to the newly allocated handle</returns>
        object AllocHandle(uint size);

        /// <summary>
        /// Resizes the handle to the new size. Usually a larger size 
        /// than the current size.
        /// </summary>
        /// <param name="handle">A referrence to the existing handle</param>
        /// <param name="newSize"></param>
        void ResizeHandle(object handle, uint newSize);

        /// <summary>
        /// Frees the prevously allocated handle
        /// </summary>
        /// <param name="handle">A referrence to the previously allocated handle</param>
        void FreeHandle(object handle);

        /// <summary>
        /// Pins the handle to the specified offset and returns a 
        /// <see cref="MemoryHandle"/> to the pinned memory block.
        /// </summary>
        /// <param name="handle"></param>
        /// <param name="offset"></param>
        /// <returns></returns>
        MemoryHandle PinHandle(object handle, int offset);

        /// <summary>
        /// Gets the full usable size of the memory block held by the handle
        /// </summary>
        /// <returns>The number of bytes available for access</returns>
        uint GetHandleSize(object handle);

        /// <summary>
        /// Gets a segment of the memory block held by the handle for reading/writing
        /// </summary>
        /// <param name="handle">A referrence to the handle object</param>
        /// <param name="offset">The data offset in bytes for the start of the desired memory block</param>
        /// <param name="length">The desired size of the block in bytes</param>
        /// <returns>A span with the desired offset of the desired length</returns>
        Span<byte> GetSpan(object handle, uint offset, uint length);
    }
}
