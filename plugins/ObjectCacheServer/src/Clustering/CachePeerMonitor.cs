/*
* Copyright (c) 2024 Vaughn Nugent
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
using System.Threading;
using System.Threading.Tasks;
using System.Collections.Generic;

using VNLib.Utils;
using VNLib.Utils.Extensions;

namespace VNLib.Data.Caching.ObjectCache.Server.Clustering
{

    internal sealed class CachePeerMonitor : VnDisposeable, IPeerMonitor
    {

        private readonly List<ICachePeer> peers = new();
        private readonly ManualResetEvent newPeerTrigger = new (false);

        /// <summary>
        /// Waits for new peers to connect to the server
        /// </summary>
        /// <returns>A task that complets when a new peer has connected</returns>
        public async Task WaitForChangeAsync()
        {
            await newPeerTrigger.WaitAsync();

            //Reset the trigger for next call
            newPeerTrigger.Reset();
        }

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
                peers.Add(peer);
            }

            //Trigger monitor when change occurs
            if(peer.Advertisment != null)
            {
                newPeerTrigger.Set();
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

        protected override void Free()
        {
            peers.Clear();
            newPeerTrigger.Dispose();
        }
    }
}
