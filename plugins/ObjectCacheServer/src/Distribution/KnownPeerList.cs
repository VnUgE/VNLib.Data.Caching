/*
* Copyright (c) 2023 Vaughn Nugent
* 
* Library: VNLib
* Package: ObjectCacheServer
* File: KnownPeerList.cs 
*
* KnownPeerList.cs is part of ObjectCacheServer which is part of the larger 
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
using System.Text.Json.Serialization;

using VNLib.Plugins;
using VNLib.Data.Caching.Extensions;
using VNLib.Plugins.Extensions.Loading;


namespace VNLib.Data.Caching.ObjectCache.Server.Distribution
{
    [ConfigurationName("known_peers")]
    internal sealed class KnownPeerList
    {
        private readonly List<KnownPeer> _peers;

        public KnownPeerList(PluginBase plugin, IConfigScope config)
        {
            //Deserialze the known peers into an array
            KnownPeer[] peers = config.Deserialze<KnownPeer[]>();

            foreach (KnownPeer peer in peers)
            {
                //Validate the peer
                peer.Validate();
            }

            _peers = peers?.ToList() ?? new();
        }

        public IEnumerable<ICacheNodeAdvertisment> GetPeers()
        {
            return _peers;
        }

        private sealed class KnownPeer : ICacheNodeAdvertisment
        {
            public Uri? ConnectEndpoint { get; set; }
            public Uri? DiscoveryEndpoint { get; set; }

            [JsonPropertyName("node_id")]
            public string NodeId { get; set; }

            [JsonPropertyName("connect_url")]
            public string? ConnectEpPath
            {
                get => ConnectEndpoint?.ToString() ?? string.Empty;
                set => ConnectEndpoint = new Uri(value ?? string.Empty);
            }

            [JsonPropertyName("discovery_url")]
            public string? DiscoveryEpPath
            {
                get => DiscoveryEndpoint?.ToString() ?? string.Empty;
                set => DiscoveryEndpoint = new Uri(value ?? string.Empty);
            }

            public void Validate()
            {
                if (string.IsNullOrWhiteSpace(NodeId))
                {
                    throw new ArgumentException("Node ID cannot be null or whitespace");
                }
                if (ConnectEndpoint is null)
                {
                    throw new ArgumentException("Connect endpoint cannot be null");
                }
                if (DiscoveryEndpoint is null)
                {
                    throw new ArgumentException("Discovery endpoint cannot be null");
                }
            }
        }
    }
}
