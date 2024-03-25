/*
* Copyright (c) 2024 Vaughn Nugent
* 
* Library: VNLib
* Package: VNLib.Data.Caching.ObjectCache
* File: BlobCacheListenerConfig.cs 
*
* BlobCacheListenerConfig.cs is part of VNLib.Data.Caching.ObjectCache which is part of the larger 
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

using VNLib.Utils.Logging;
using VNLib.Net.Messaging.FBM;

namespace VNLib.Data.Caching.ObjectCache
{
    /// <summary>
    /// A configuration object for <see cref="BlobCacheListener{T}"/>
    /// </summary>
    public sealed record class BlobCacheListenerConfig
    {
        /// <summary>
        /// Writes error and debug logging information
        /// </summary>
        public ILogProvider Log { get; init; } = null!;

        /// <summary>
        /// The memory manager used for the internal FBM server listener
        /// </summary>
        public IFBMMemoryManager MemoryManager { get; init; } = null!;

        /// <summary>
        /// A flag that enables verifying and sending checksums with message
        /// data in FBM header fields
        /// </summary>
        public bool EnableMessageChecksums { get; init; } = true;

        /// <summary>
        /// A flag that enables logging of transactions (events) to the log
        /// </summary>
        public bool LogTransactions { get; init; }
    }
}
