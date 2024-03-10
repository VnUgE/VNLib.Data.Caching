/*
* Copyright (c) 2024 Vaughn Nugent
* 
* Library: VNLib
* Package: VNLib.Data.Caching.Extensions
* File: CacheNodeConfiguration.cs 
*
* CacheNodeConfiguration.cs is part of VNLib.Data.Caching.Extensions which is part of the larger 
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
using System.Runtime.CompilerServices;

namespace VNLib.Data.Caching.Extensions.Clustering
{

    /// <summary>
    /// A cache configuration for cache servers (nodes)
    /// </summary>
    public class CacheNodeConfiguration : CacheClientConfiguration
    {
        /// <summary>
        /// The address for clients to connect to
        /// </summary>
        public Uri? ConnectEndpoint { get; private set; }

        /// <summary>
        /// Whether or not to advertise ourself to peer nodes
        /// </summary>
        public bool BroadcastAdverisment { get; private set; }

        /// <summary>
        /// Define the endpoint for clients to connect to to discover
        /// other discovertable nodes
        /// </summary>
        public Uri? DiscoveryEndpoint { get; private set; }

        /// <summary>
        /// Gets the configuration for this node as an advertisment
        /// </summary>
        public CacheNodeAdvertisment Advertisment
        {
            get
            {
                return new CacheNodeAdvertisment()
                {
                    DiscoveryEndpoint = DiscoveryEndpoint,
                    ConnectEndpoint = ConnectEndpoint,
                    NodeId = NodeId
                };
            }
        }

        /// <summary>
        /// Sets the full address of our cache endpoint for clients to connect to
        /// </summary>
        /// <param name="connectUri">The uri clients will attempt to connect to</param>
        public CacheNodeConfiguration WithCacheEndpoint(Uri connectUri)
        {
            ConnectEndpoint = connectUri;
            return this;
        }

        /// <summary>
        /// Enables or disables the advertisement of this node to other nodes
        /// </summary>
        /// <param name="discoveryEndpoint">The absolute endpoint clients will use to connect to</param>
        public CacheNodeConfiguration EnableAdvertisment(Uri? discoveryEndpoint)
        {
            BroadcastAdverisment = discoveryEndpoint != null;
            DiscoveryEndpoint = discoveryEndpoint;
            return this;
        }

        internal StrongBox<string> NodeIdRef { get; } = new(string.Empty);

        ///<inheritdoc/>
        public string NodeId
        {
            get => NodeIdRef.Value!;
            private set => NodeIdRef.Value = value;
        }       

        /// <summary>
        /// Specifies the current server's cluster node id. If this 
        /// is a server connection attempting to listen for changes on the
        /// remote server, this id must be set and unique
        /// </summary>
        /// <param name="nodeId">The cluster node id of the current server</param>
        /// <returns>Chainable fluent object</returns>
        /// <exception cref="ArgumentNullException"></exception>
        public CacheNodeConfiguration WithNodeId(string nodeId)
        {
            NodeId = nodeId ?? throw new ArgumentNullException(nameof(nodeId));
            return this;
        }

    }
}
