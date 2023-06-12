/*
* Copyright (c) 2023 Vaughn Nugent
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


namespace VNLib.Data.Caching.Extensions
{

    public class CacheNodeConfiguration: CacheClientConfiguration, ICachePeerAdvertisment
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
        /// Sets the full address of our cache endpoint for clients to connect to
        /// </summary>
        /// <param name="connectUri">The uri clients will attempt to connect to</param>
        public CacheNodeConfiguration WithCacheEndpoint(Uri connectUri)
        {
            ConnectEndpoint = connectUri;
            return this;
        }

        public CacheNodeConfiguration EnableAdvertisment(bool enable, Uri? discoveryEndpoint)
        {
            BroadcastAdverisment = enable;
            DiscoveryEndpoint = discoveryEndpoint;
            return this;
        }

        ///<inheritdoc/>
        public string NodeId { get; private set; } = null!;

        /// <summary>
        /// Specifies the current server's cluster node id. If this 
        /// is a server connection attempting to listen for changes on the
        /// remote server, this id must be set and unique
        /// </summary>
        /// <param name="nodeId">The cluster node id of the current server</param>
        /// <returns>Chainable fluent object</returns>
        /// <exception cref="ArgumentNullException"></exception>
        public CacheClientConfiguration WithNodeId(string nodeId)
        {
            NodeId = nodeId ?? throw new ArgumentNullException(nameof(nodeId));
            return this;
        }

    }
}
