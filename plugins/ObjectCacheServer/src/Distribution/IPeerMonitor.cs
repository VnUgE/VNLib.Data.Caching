/*
* Copyright (c) 2023 Vaughn Nugent
* 
* Library: VNLib
* Package: ObjectCacheServer
* File: IPeerMonitor.cs 
*
* IPeerMonitor.cs is part of ObjectCacheServer which is part of the larger 
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

using System.Collections.Generic;

namespace VNLib.Data.Caching.ObjectCache.Server.Distribution
{
    /// <summary>
    /// Represents a monitor for peer cache servers to advertise their presence
    /// in the cluster
    /// </summary>
    internal interface IPeerMonitor
    {
        /// <summary>
        /// Notifies the monitor that a peer has connected to the cluster
        /// </summary>
        /// <param name="peer">The peer that connected</param>
        void OnPeerConnected(ICachePeer peer);

        /// <summary>
        /// Notifies the monitor that a peer has disconnected
        /// </summary>
        /// <param name="peer">The peer that has disconnected</param>
        void OnPeerDisconnected(ICachePeer peer);

        /// <summary>
        /// Gets an enumerable of all peers currently connected to this node
        /// </summary>
        /// <returns>The collection of all connected peers</returns>
        IEnumerable<ICachePeer> GetAllPeers();
    }
}
