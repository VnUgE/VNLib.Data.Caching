/*
* Copyright (c) 2023 Vaughn Nugent
* 
* Library: VNLib
* Package: VNLib.Data.Caching.ObjectCache
* File: IBlobCacheTable.cs 
*
* IBlobCacheTable.cs is part of VNLib.Data.Caching.ObjectCache which is part of the larger 
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
using System.Collections.Generic;

namespace VNLib.Data.Caching.ObjectCache
{
    /// <summary>
    /// A table that contains a collection of blob cache buckets 
    /// for improved cache concurrency 
    /// </summary>
    public interface IBlobCacheTable : IEnumerable<IBlobCacheBucket>, IDisposable
    {
        /// <summary>
        /// Gets a bucket that should contain the object by its id
        /// </summary>
        /// <param name="objectId">The id of the object to get the bucket for</param>
        /// <returns>The bucket that should contain the object</returns>
        IBlobCacheBucket GetBucket(ReadOnlySpan<char> objectId);
    }
}
