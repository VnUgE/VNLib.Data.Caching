/*
* Copyright (c) 2023 Vaughn Nugent
* 
* Library: VNLib
* Package: VNLib.Data.Caching.Extensions
* File: CacheNodeAdvertisment.cs 
*
* CacheNodeAdvertisment.cs is part of VNLib.Data.Caching.Extensions which is part of the larger 
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
using System.Text.Json.Serialization;

namespace VNLib.Data.Caching.Extensions.Clustering
{
    /// <summary>
    /// Represents a node that can be advertised to clients
    /// </summary>
    public class CacheNodeAdvertisment : IEquatable<CacheNodeAdvertisment>
    {
        /// <summary>
        /// The endpoint for clients to connect to to access the cache
        /// </summary>
        [JsonIgnore]
        public Uri? ConnectEndpoint { get; set; }

        /// <summary>
        /// Gets the address for clients to connect to to discover other discovertable nodes
        /// </summary>
        [JsonIgnore]
        public Uri? DiscoveryEndpoint { get; set; }

        /// <summary>
        /// Gets the unique identifier for this node
        /// </summary>
        [JsonPropertyName("iss")]
        public string NodeId { get; set; }

        [JsonPropertyName("url")]
        public string? url
        {
            get => ConnectEndpoint?.ToString();
            set => ConnectEndpoint = value == null ? null : new Uri(value);
        }

        [JsonPropertyName("dis")]
        public string? dis
        {
            get => DiscoveryEndpoint?.ToString();
            set => DiscoveryEndpoint = value == null ? null : new Uri(value);
        }

        /// <summary>
        /// Determines if the given node is equal to this node, by comparing the node ids
        /// </summary>
        /// <param name="obj">The other node advertisment to compare</param>
        /// <returns>True if the nodes are equal, false otherwise</returns>
        public override bool Equals(object? obj) => obj is CacheNodeAdvertisment ad && Equals(ad);

        /// <summary>
        /// Gets the hash code for this node, based on the node id
        /// </summary>
        /// <returns>The instance hash-code</returns>
        public override int GetHashCode() => string.GetHashCode(NodeId, StringComparison.OrdinalIgnoreCase);

        /// <summary>
        /// Determines if the given node is equal to this node, by comparing the node ids
        /// </summary>
        /// <param name="other">The other node advertisment to compare</param>
        /// <returns>True if the nodes are equal, false otherwise</returns>
        public bool Equals(CacheNodeAdvertisment? other) => string.Equals(NodeId, other?.NodeId, StringComparison.OrdinalIgnoreCase);

        /// <summary>
        /// Formats a string representation of this node
        /// </summary>
        /// <returns>The formatted information string</returns>
        public override string ToString()
        {
            return $"NodeId: {NodeId} Connect: {url}, Discover?: {dis}";
        }
    }
}
