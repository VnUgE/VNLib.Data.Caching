/*
* Copyright (c) 2023 Vaughn Nugent
* 
* Library: VNLib
* Package: VNLib.Data.Caching.ObjectCache
* File: CacheBucketHandle.cs 
*
* CacheBucketHandle.cs is part of VNLib.Data.Caching.ObjectCache which is part of the larger 
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
using System.Diagnostics.CodeAnalysis;

namespace VNLib.Data.Caching.ObjectCache
{
    /// <summary>
    /// Holds an exclusive lock on a <see cref="IBlobCacheBucket"/>, and exposes
    /// acess to its internal <see cref="IBlobCache"/>
    /// </summary>
    public readonly struct CacheBucketHandle : IDisposable, IEquatable<CacheBucketHandle> 
    {
        private readonly IBlobCacheBucket? _bucket;

        /// <summary>
        /// The <see cref="IBlobCache"/> held by the current handle
        /// </summary>
        public readonly IBlobCache Cache { get; }

        /// <summary>
        /// Initializes an empty blobcache handle
        /// </summary>
        public CacheBucketHandle()
        {
            _bucket = null;
            Cache = null!;
        }

        /// <summary>
        /// Creates a new bucket lock handle to be released on dispose
        /// </summary>
        /// <param name="bucket">The bucket to release access to on dispose</param>
        /// <param name="cache">The underlying <see cref="IBlobCache"/> provide exclusive access to</param>
        public CacheBucketHandle(IBlobCacheBucket bucket, IBlobCache cache)
        {
            _bucket = bucket;
            Cache = cache;
        }

        /// <summary>
        /// Releases the exlusive lock held on the bucket
        /// </summary>
        public void Dispose()
        {
            //Release the bucket when disposed
            _bucket?.Release();
        }

        /// <summary>
        /// Determines if the other handle instance is equal to the current. Handles are 
        /// equal iff the underlying bucket referrence is equal.
        /// </summary>
        /// <param name="other">The other handle to compare</param>
        /// <returns>True if the handles hold a referrence to the same bucket</returns>
        public bool Equals(CacheBucketHandle other) => ReferenceEquals(_bucket, other._bucket);

        /// <summary>
        /// Determines if the other handle instance is equal to the current. Handles are 
        /// equal iff the underlying bucket referrence is equal.
        /// </summary>
        /// <param name="obj">The other handle to compare</param>
        /// <returns>True if the handles hold a referrence to the same bucket</returns>
        public override bool Equals([NotNullWhen(true)] object? obj) => obj is CacheBucketHandle other && Equals(other);

        /// <summary>
        /// Gets the hashcode of the underlying bucket
        /// </summary>
        /// <returns></returns>
        public override int GetHashCode() => _bucket?.GetHashCode() ?? -1;

        /// <summary>
        /// Determines if the handles are equal by the <see cref="Equals(CacheBucketHandle)"/>
        /// method.
        /// </summary>
        /// <param name="left"></param>
        /// <param name="right"></param>
        /// <returns>True if the internal bucket references are equal</returns>
        public static bool operator ==(CacheBucketHandle left, CacheBucketHandle right) => left.Equals(right);

        /// <summary>
        /// Determines if the handles are equal by the <see cref="Equals(CacheBucketHandle)"/>
        /// method.
        /// </summary>
        /// <param name="left"></param>
        /// <param name="right"></param>
        /// <returns>True if the internal bucket references are NOT equal</returns>
        public static bool operator !=(CacheBucketHandle left, CacheBucketHandle right) => !(left == right);
    }
}
