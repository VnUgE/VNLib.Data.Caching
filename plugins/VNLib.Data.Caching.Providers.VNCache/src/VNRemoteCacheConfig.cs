﻿/*
* Copyright (c) 2025 Vaughn Nugent
* 
* Library: VNLib
* Package: VNLib.Data.Caching.Providers.VNCache
* File: VNRemoteCacheConfig.cs 
*
* VNRemoteCacheConfig.cs is part of VNLib.Data.Caching.Providers.VNCache which is part of the larger 
* VNLib collection of libraries and utilities.
*
* VNLib.Data.Caching.Providers.VNCache is free software: you can redistribute it and/or modify 
* it under the terms of the GNU Affero General Public License as 
* published by the Free Software Foundation, either version 3 of the
* License, or (at your option) any later version.
*
* VNLib.Data.Caching.Providers.VNCache is distributed in the hope that it will be useful,
* but WITHOUT ANY WARRANTY; without even the implied warranty of
* MERCHANTABILITY or FITNESS FOR A PARTICULAR PURPOSE. See the
* GNU Affero General Public License for more details.
*
* You should have received a copy of the GNU Affero General Public License
* along with this program.  If not, see https://www.gnu.org/licenses/.
*/

using System;
using System.Linq;
using System.Text.Json.Serialization;

using VNLib.Utils.Logging;
using VNLib.Data.Caching.Extensions;
using VNLib.Plugins.Extensions.Loading.Configuration;

namespace VNLib.Data.Caching.Providers.VNCache
{
    /// <summary>
    /// Represents a remote VNCache client configuration
    /// </summary>
    public class VNRemoteCacheConfig : VNCacheConfig
    {
        const string DefaultWellKnownEndpoint = "/.well-known/vncache";

        /// <summary>
        /// Specifies a logging provider for the the cache client to write 
        /// internal debugging information to. This log is strictly for 
        /// client internal debugging.
        /// </summary>
        [JsonIgnore]
        public ILogProvider? ClientDebugLog { get; set; }

        /// <summary>
        /// The authentication manager for the cache client
        /// </summary>
        [JsonIgnore]
        public ICacheAuthManager AuthManager { get; set; } = null!;

        /// <summary>
        /// The broker server address
        /// </summary>
        [JsonPropertyName("use_tls")]
        public bool UseTls { get; set; } = true;

        /// <summary>
        /// The time (in seconds) to randomly delay polling the broker server
        /// for available servers
        /// </summary>
        [JsonPropertyName("discovery_interval_sec")]
        public int? DiscoveryIntervalSeconds { get; set; }

        /// <summary>
        /// The maximum time (in seconds) for FBM cache operations are allowed
        /// to take before timing out. 
        /// </summary>
        /// <remarks>
        /// NOTE: You should set this value to something reasonable as FBM messages can 
        /// be lost and cause deadlocks if your cache implementation does not rely on
        /// CancellationTokens 
        /// </remarks>
        [JsonPropertyName("request_timeout_sec")]
        public int? RequestTimeoutSeconds { get; set; }

        /// <summary>
        /// Retry interval in a timespan
        /// </summary>
        internal TimeSpan DiscoveryInterval => TimeSpan.FromSeconds(DiscoveryIntervalSeconds!.Value);

        /// <summary>
        /// FBM Request timeout
        /// </summary>
        internal TimeSpan RequestTimeout => TimeSpan.FromSeconds(RequestTimeoutSeconds!.Value);

        /// <summary>
        /// The time in milliseconds for the initial node delay
        /// </summary>
        [JsonPropertyName("intial_delay_ms")]
        public uint? InitialNodeDelay { get; set; }

        /// <summary>
        /// The initial peers to connect to
        /// </summary>
        [JsonPropertyName("initial_nodes")]
        public string[]? InitialNodes { get; set; }

        /// <summary>
        /// Gets the initial nodes as a collection of URIs
        /// </summary>
        /// <returns>The nodes as a collection of URIs</returns>
        /// <exception cref="InvalidOperationException"></exception>
        public Uri[] GetInitialNodeUris()
        {
            Validate.NotNull(InitialNodes, "Initial nodes have not been set");

            return InitialNodes.Select(static x =>
                {
                    //Append a default well known endpoint if the path is just a root
                    Uri ur = new (x, UriKind.Absolute);
                    return ur.LocalPath == "/" ? new Uri(ur, DefaultWellKnownEndpoint) : ur;
                })
                .ToArray();
        }

        ///<inheritdoc/>
        public override void OnValidate()
        {
            base.OnValidate();

            Validate.Assert(DiscoveryIntervalSeconds.HasValue, "A discovery interval is required");
            Validate.Range(DiscoveryIntervalSeconds.Value, 1, int.MaxValue);

            Validate.Assert(RequestTimeoutSeconds.HasValue, "A request timeout is required");
            Validate.Range(RequestTimeoutSeconds.Value, 1, int.MaxValue);

            Validate.NotNull(InitialNodes, "You must specify at least one initial cache node to connect to");
            Validate.Assert(InitialNodes.Length > 0, "You must specify at least one initial cache node to connect to");

            Validate.NotNull(AuthManager, "You must configure an authentication manager");

            //Validate initial nodes
            foreach (Uri peer in GetInitialNodeUris())
            {
                Validate.Assert(peer.IsAbsoluteUri, "You must specify an absolute URI for each initial node");
                Validate.Assert(
                    peer.Scheme == Uri.UriSchemeHttp || peer.Scheme == Uri.UriSchemeHttps, 
                    message: "You must specify an HTTP or HTTPS URI for each initial node"
                );
            }
        }
    }
}