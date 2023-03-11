/*
* Copyright (c) 2023 Vaughn Nugent
* 
* Library: VNLib
* Package: VNLib.Data.Caching.ObjectCache
* File: IMemoryCacheEntryFactory.cs 
*
* IMemoryCacheEntryFactory.cs is part of VNLib.Data.Caching.ObjectCache which 
* is part of the larger VNLib collection of libraries and utilities.
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

namespace VNLib.Data.Caching.ObjectCache
{
    /// <summary>
    /// A factory abstraction that builds <see cref="CacheEntry"/> structures
    /// linked to internally configured memory implementations, for cache 
    /// promotions.
    /// </summary>
    public interface IMemoryCacheEntryFactory
    {
        /// <summary>
        /// Creates and initalizes a new <see cref="CacheEntry"/> from the desired object data
        /// </summary>
        /// <param name="entryData">The non-owned memory to copy into the the new <see cref="CacheEntry"/></param>
        /// <returns>The newly initalized <see cref="CacheEntry"/></returns>
        CacheEntry CreateEntry(ReadOnlySpan<byte> entryData);
    }
}
