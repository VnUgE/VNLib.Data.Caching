/*
* Copyright (c) 2023 Vaughn Nugent
* 
* Library: VNLib
* Package: VNLib.Data.Caching.ObjectCache
* File: IBlobCacheBucket.cs 
*
* IBlobCacheBucket.cs is part of VNLib.Data.Caching.ObjectCache which 
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
using System.Threading;
using System.Threading.Tasks;

namespace VNLib.Data.Caching.ObjectCache
{
    /// <summary>
    /// Represents a singe cache bucket that maintains cache concurrent <see cref="IBlobCache"/>
    /// operations.
    /// </summary>
    public interface IBlobCacheBucket : IDisposable
    {
        /// <summary>
        /// The unique integer id of a bucket within an <see cref="IBlobCacheTable"/>
        /// </summary>
        uint Id { get; }

        /// <summary>
        /// Allows for waiting for the cache directly, IE without receiving a lock handle
        /// </summary>
        /// <param name="cancellation"></param>
        /// <returns>The underlying <see cref="IBlobCache"/> that now has exlcusive access</returns>
        ValueTask<IBlobCache> ManualWaitAsync(CancellationToken cancellation);

        /// <summary>
        /// Releases an exlcusive lock on the current bucket that was obtained by <see cref="ManualWaitAsync(CancellationToken)"/>
        /// </summary>
        void Release();
    }
}
