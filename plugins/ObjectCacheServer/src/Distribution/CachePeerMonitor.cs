/*
* Copyright (c) 2023 Vaughn Nugent
* 
* Library: VNLib
* Package: ObjectCacheServer
* File: CachePeerMonitor.cs 
*
* CachePeerMonitor.cs is part of ObjectCacheServer which is part of the larger 
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

using System.Linq;
using System.Collections.Generic;

using VNLib.Plugins;

namespace VNLib.Data.Caching.ObjectCache.Server.Distribution
{

    internal sealed class CachePeerMonitor : IPeerMonitor
    {

        private readonly LinkedList<ICachePeer> peers = new();

        public CachePeerMonitor(PluginBase plugin)
        { }

        ///<inheritdoc/>
        public IEnumerable<ICachePeer> GetAllPeers()
        {
            lock(peers)
            {
                return peers.ToArray();
            }
        }

        ///<inheritdoc/>
        public void OnPeerConnected(ICachePeer peer)
        {
            //When a peer is connected we can add it to the list so the replication manager can see it
            lock(peers)
            {
                peers.AddLast(peer);
            }
        }

        ///<inheritdoc/>
        public void OnPeerDisconnected(ICachePeer peer)
        {
            //When a peer is disconnected we can remove it from the list
            lock(peers)
            {
                peers.Remove(peer);
            }
        }
    }
}
