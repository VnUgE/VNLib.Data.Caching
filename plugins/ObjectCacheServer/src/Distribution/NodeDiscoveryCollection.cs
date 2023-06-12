/*
* Copyright (c) 2023 Vaughn Nugent
* 
* Library: VNLib
* Package: ObjectCacheServer
* File: PeerDiscoveryManager.cs 
*
* PeerDiscoveryManager.cs is part of ObjectCacheServer which is part of the larger 
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

using System;
using System.Linq;
using System.Collections.Generic;

using VNLib.Plugins;
using VNLib.Data.Caching.Extensions;

namespace VNLib.Data.Caching.ObjectCache.Server.Distribution
{
    sealed class NodeDiscoveryCollection : INodeDiscoveryCollection
    {
        private LinkedList<ICachePeerAdvertisment> _peers;


        public NodeDiscoveryCollection(PluginBase plugin)
        {
            _peers = new();   
        }       

        ///<inheritdoc/>
        public INodeDiscoveryEnumerator BeginDiscovery()
        {
            return new NodeEnumerator(new());
        }

        ///<inheritdoc/>
        public INodeDiscoveryEnumerator BeginDiscovery(IEnumerable<ICachePeerAdvertisment> initialPeers)
        {
            //Init new enumerator with the initial peers
            return new NodeEnumerator(new(initialPeers));
        }

        ///<inheritdoc/>
        public void CompleteDiscovery(INodeDiscoveryEnumerator enumerator)
        {
            _ = enumerator ?? throw new ArgumentNullException(nameof(enumerator));

            //Capture all nodes from the enumerator and store them as our current peers
            _peers = (enumerator as NodeEnumerator)!.Peers;
        }

        ///<inheritdoc/>
        public ICachePeerAdvertisment[] GetAllNodes()
        {
            //Capture all current peers
            return _peers.ToArray();
        }

        private sealed record class NodeEnumerator(LinkedList<ICachePeerAdvertisment> Peers) : INodeDiscoveryEnumerator
        {
            //Keep track of the current node in the collection so we can move down the list
            private LinkedListNode<ICachePeerAdvertisment>? _currentNode = Peers.First;

            public ICachePeerAdvertisment? GetNextPeer()
            {
                //Move to the next peer in the collection
                _currentNode = _currentNode?.Next;

                return _currentNode?.Value;
            }

            public void OnPeerDiscoveryComplete(IEnumerable<ICachePeerAdvertisment> discoveredPeers)
            {
                //Get only the peers from the discovery that are not already in the collection
                IEnumerable<ICachePeerAdvertisment> newPeers = discoveredPeers.Except(Peers);
                
                //Add them to the end of the collection 
                foreach(ICachePeerAdvertisment ad in newPeers)
                {
                    Peers.AddLast(ad);
                }
            }
        }
    }
}
