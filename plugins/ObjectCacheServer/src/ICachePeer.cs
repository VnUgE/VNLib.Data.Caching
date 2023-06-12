/*
* Copyright (c) 2023 Vaughn Nugent
* 
* Library: VNLib
* Package: ObjectCacheServer
* File: ICachePeer.cs 
*
* ICachePeer.cs is part of ObjectCacheServer which is part of the larger 
* VNLib collection of libraries and utilities.
*
* ObjectCacheServer is free software: you can redistribute it and/or modify 
* it under the terms of the GNU Affero General Public License as 
* published by the Free Software Foundation, either version 3 of the
* License, or (at your option) any later version.
*
* ObjectCacheServer is distributed in the hope that it will be useful,
* but WITHOUT ANY WARRANTY; without even the implied warranty of
* MERCHANTABILITY or FITNESS FOR A PARTICULAR PURPOSE. See the
* GNU Affero General Public License for more details.
*
* You should have received a copy of the GNU Affero General Public License
* along with this program.  If not, see https://www.gnu.org/licenses/.
*/

using VNLib.Data.Caching.Extensions;

namespace VNLib.Data.Caching.ObjectCache.Server
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
        ICachePeerAdvertisment? Advertisment { get; }
    }
}
