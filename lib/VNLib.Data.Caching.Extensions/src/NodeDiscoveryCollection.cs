/*
* Copyright (c) 2023 Vaughn Nugent
* 
* Library: VNLib
* Package: VNLib.Data.Caching.Extensions
* File: INodeDiscoveryCollection.cs 
*
* INodeDiscoveryCollection.cs is part of VNLib.Data.Caching.Extensions which is part of the larger 
* VNLib collection of libraries and utilities.
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
using System.Collections.Generic;
using System.Collections;

namespace VNLib.Data.Caching.Extensions
{
    /// <summary>
    /// Represents a collection of available cache nodes from a discovery process
    /// </summary>
    public sealed class NodeDiscoveryCollection : INodeDiscoveryCollection
    {
        private LinkedList<ICacheNodeAdvertisment> _peers;

        /// <summary>
        /// Initializes a new empty <see cref="NodeDiscoveryCollection"/>
        /// </summary>
        public NodeDiscoveryCollection()
        {
            _peers = new();
        }

        ///<inheritdoc/>
        public INodeDiscoveryEnumerator BeginDiscovery()
        {
            return new NodeEnumerator(new());
        }

        ///<inheritdoc/>
        public INodeDiscoveryEnumerator BeginDiscovery(IEnumerable<ICacheNodeAdvertisment> initialPeers)
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
        public ICacheNodeAdvertisment[] GetAllNodes()
        {
            //Capture all current peers
            return _peers.ToArray();
        }

        private sealed record class NodeEnumerator(LinkedList<ICacheNodeAdvertisment> Peers) : INodeDiscoveryEnumerator
        {
            //Keep track of the current node in the collection so we can move down the list
            private LinkedListNode<ICacheNodeAdvertisment>? _currentNode = Peers.First;

            public ICacheNodeAdvertisment Current => _currentNode?.Value;
            object IEnumerator.Current => _currentNode?.Value;


            ///<inheritdoc/>
            public bool MoveNext()
            {
                //Move to the next peer in the collection
                _currentNode = _currentNode?.Next;

                return _currentNode?.Value != null;
            }

            ///<inheritdoc/>
            public void OnPeerDiscoveryComplete(IEnumerable<ICacheNodeAdvertisment> discoveredPeers)
            {
                //Get only the peers from the discovery that are not already in the collection
                IEnumerable<ICacheNodeAdvertisment> newPeers = discoveredPeers.Except(Peers);

                //Add them to the end of the collection 
                foreach (ICacheNodeAdvertisment ad in newPeers)
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
