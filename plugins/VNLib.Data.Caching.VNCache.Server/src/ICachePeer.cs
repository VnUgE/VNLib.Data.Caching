/*
* Copyright (c) 2026 Vaughn Nugent
* 
* Library: VNLib
* Package: VNLib.Data.Caching.VNCache.Server
* File: ICachePeer.cs 
*
* ICachePeer.cs is part of VNLib.Data.Caching.VNCache.Server which is part of the larger 
* VNLib collection of libraries and utilities.
*
* VNLib.Data.Caching.VNCache.Server is free software: you can redistribute it and/or modify 
* it under the terms of the GNU Affero General Public License as 
* published by the Free Software Foundation, either version 3 of the
* License, or (at your option) any later version.
*
* VNLib.Data.Caching.VNCache.Server is distributed in the hope that it will be useful,
* but WITHOUT ANY WARRANTY; without even the implied warranty of
* MERCHANTABILITY or FITNESS FOR A PARTICULAR PURPOSE. See the
* GNU Affero General Public License for more details.
*
* You should have received a copy of the GNU Affero General Public License
* along with this program.  If not, see https://www.gnu.org/licenses/.
*/

using VNLib.Data.Caching.VNCache.Clustering;

namespace VNLib.Data.Caching.VNCache.Server
{
    /// <summary>
    /// Represents a fellow cache peer in the cluster
    /// </summary>
    internal interface ICachePeer
    {
        /// <summary>
        /// The unique identifier of the node
        /// </summary>
        string NodeId { get; }

        /// <summary>
        /// An optional signed advertisment message for other peers
        /// </summary>
        CacheNodeAdvertisment? Advertisment { get; }
    }
}
