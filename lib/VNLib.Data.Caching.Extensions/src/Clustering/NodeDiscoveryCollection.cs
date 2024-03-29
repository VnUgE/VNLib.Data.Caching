﻿/*
* Copyright (c) 2024 Vaughn Nugent
* 
* Library: VNLib
* Package: VNLib.Data.Caching.Extensions
* File: NodeDiscoveryCollection.cs 
*
* NodeDiscoveryCollection.cs is part of VNLib.Data.Caching.Extensions which is part 
* of the larger VNLib collection of libraries and utilities.
*
* VNLib.Data.Caching.Extensions is free software: you can redistribute it and/or modify 
* it under the terms of the GNU Affero General Public License as 
* published by the Free Software Foundation, either version 3 of the
* License, or (at your option) any later version.
*
* VNLib.Data.Caching.Extensions is distributed in the hope that it will be useful,
* but WITHOUT ANY WARRANTY; without even the implied warranty of
* MERCHANTABILITY or FITNESS FOR A PARTICULAR PURPOSE. See the
* GNU Affero General Public License for more details.
*
* You should have received a copy of the GNU Affero General Public License
* along with this program.  If not, see https://www.gnu.org/licenses/.
*/

using System;
using System.Linq;
using System.Collections;
using System.Collections.Generic;
using System.Runtime.CompilerServices;

using VNLib.Utils.Extensions;

namespace VNLib.Data.Caching.Extensions.Clustering
{
    /// <summary>
    /// Represents a collection of available cache nodes from a discovery process
    /// </summary>
    public sealed class NodeDiscoveryCollection(StrongBox<string?>? selfId) : INodeDiscoveryCollection
    {
        private LinkedList<CacheNodeAdvertisment> _peers = new();

        /// <summary>
        /// Manually adds nodes to the collection that were not discovered through the discovery process
        /// </summary>
        /// <param name="nodes">The nodes to add</param>
        public void AddManualNodes(IEnumerable<CacheNodeAdvertisment> nodes)
        {
            //Get only the nodes that are not already in the collection
            IEnumerable<CacheNodeAdvertisment> newPeers = nodes.Except(_peers);

            //Add them to the end of the collection
            foreach (CacheNodeAdvertisment peer in newPeers)
            {
                _peers.AddLast(peer);
            }
        }

        /// <summary>
        /// Removes a vector of nodes from the internal collection
        /// </summary>
        /// <param name="nodes">The vector containg nodes to remove from the collection</param>
        public void RemoveManualNodes(IEnumerable<CacheNodeAdvertisment> nodes) => nodes.ForEach(n => _peers.Remove(n));

        ///<inheritdoc/>
        public INodeDiscoveryEnumerator BeginDiscovery() => new NodeEnumerator(new(), selfId?.Value);

        ///<inheritdoc/>
        public INodeDiscoveryEnumerator BeginDiscovery(IEnumerable<CacheNodeAdvertisment> initialPeers)
        {
            ArgumentNullException.ThrowIfNull(initialPeers);

            //Init new enumerator with the initial peers
            return new NodeEnumerator(new(initialPeers), selfId?.Value);
        }

        ///<inheritdoc/>
        public void CompleteDiscovery(INodeDiscoveryEnumerator enumerator)
        {
            ArgumentNullException.ThrowIfNull(enumerator);  

            //Capture all nodes from the enumerator and store them as our current peers
            _peers = (enumerator as NodeEnumerator)!.Peers;
        }

        ///<inheritdoc/>
        public CacheNodeAdvertisment[] GetAllNodes() => _peers.ToArray();

        private sealed record class NodeEnumerator(LinkedList<CacheNodeAdvertisment> Peers, string? SelfNodeId) : INodeDiscoveryEnumerator
        {
            private bool isInit;

            //Keep track of the current node in the collection so we can move down the list
            private LinkedListNode<CacheNodeAdvertisment>? _currentNode;

            public CacheNodeAdvertisment Current => _currentNode?.Value;
            object IEnumerator.Current => _currentNode?.Value;


            ///<inheritdoc/>
            public bool MoveNext()
            {
                if (!isInit)
                {
                    _currentNode = Peers.First;
                    isInit = true;
                }
                else
                {
                    //Move to the next peer in the collection
                    _currentNode = _currentNode?.Next;
                }

                return _currentNode?.Value != null;
            }

            ///<inheritdoc/>
            public void OnPeerDiscoveryComplete(IEnumerable<CacheNodeAdvertisment> discoveredPeers)
            {
                //Get only the peers from the discovery that are not already in the collection, or ourselves
                IEnumerable<CacheNodeAdvertisment> newPeers = discoveredPeers.Except(Peers);

                if (!string.IsNullOrWhiteSpace(SelfNodeId))
                {
                    //remove ourselves from the list
                    newPeers = newPeers.Where(p => !SelfNodeId.Equals(p.NodeId, StringComparison.OrdinalIgnoreCase));
                }

                //Add them to the end of the collection 
                foreach (CacheNodeAdvertisment ad in newPeers)
                {
                    Peers.AddLast(ad);
                }
            }

            public void Reset()
            {
                //Go to the first node
                _currentNode = Peers.First;
            }

            public void Dispose()
            { }
        }
    }
}
