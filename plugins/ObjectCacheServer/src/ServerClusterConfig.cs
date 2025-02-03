/*
* Copyright (c) 2024 Vaughn Nugent
* 
* Library: VNLib
* Package: ObjectCacheServer
* File: ServerClusterConfig.cs 
*
* ServerClusterConfig.cs is part of ObjectCacheServer which is part of the larger 
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
using System.Net;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Serialization;

using VNLib.Plugins;
using VNLib.Utils.Logging;
using VNLib.Plugins.Extensions.Loading;
using VNLib.Plugins.Extensions.Loading.Configuration;

using VNLib.Data.Caching.Extensions.Clustering;

namespace VNLib.Data.Caching.ObjectCache.Server
{

    internal sealed class ServerClusterConfig : IOnConfigValidation
    {
        [JsonPropertyName("queue_purge_interval_sec")]
        public uint EventPurgeIntervalSec { get; init; }

        [JsonPropertyName("max_queue_depth")]
        public int MaxQueueDepth { get; init; }

        [JsonPropertyName("connect_path")]
        public string ConnectPath { get; init; } = "/cache";

        [JsonPropertyName("well_known_path")]
        public string WellKnownPath { get; init; } = CacheConstants.DefaultWellKnownPath;

        /// <summary>
        /// Ensures that client signed connections use the same IP address
        /// between authorized connections. This should be on for most
        /// production deployments.
        /// </summary>
        [JsonPropertyName("verify_ip")]
        public bool VerifyIp { get; init; } = true;

        /// <summary>
        /// The maximum number of peer connections to allow
        /// </summary>
        [JsonPropertyName("max_peers")]
        public uint MaxPeerConnections { get; init; }


        public ushort ServerPort { get; } = config.GetRequiredProperty("local_port", p => p.GetUInt16());

        /// <summary>
        /// The maxium number of concurrent client connections to allow
        /// before rejecting new connections
        /// </summary>
        [JsonPropertyName("max_client_connections")]
        public uint MaxClientConnections { get; init; }

        [JsonPropertyName("kown_peers")]
        public string[] KownPeers { get; init; } = [];

        public void OnValidate()
        {
            Validate.Range(MaxPeerConnections, 1u, 1000u, "max_peers");
            Validate.Range(MaxClientConnections, 1u, 1000u, "max_client_connections");
            Validate.Range(MaxQueueDepth, 1, 1000, "max_queue_depth");

            //Check for valid well-known path
            Validate.NotNull(WellKnownPath, "well_known_path");
            Validate.Assert(WellKnownPath[0] == '/', "'well_known_path' must start with '/' ");

            //Check for valid connect path
            Validate.NotNull(ConnectPath, "connect_path");
            Validate.Assert(ConnectPath[0] == '/', "'connect_path' must start with '/' ");
        }

        const string CacheConfigTemplate =
@"
Cluster Configuration:
    Node Id: {id}
    TlsEndabled: {tls}
    Verify Ip: {vi}
    Well-Known: {wk}
    Cache Endpoint: {ep}
    Discovery Endpoint: {dep}
    Discovery Interval: {di}
    Max Peer Connections: {mpc}    
    Max Queue Depth: {mqd}
    Event Queue Purge Interval: {eqpi}
";

        internal CacheNodeConfiguration BuildNodeConfig()
        {
            CacheNodeConfiguration conf = new();

            //Get the port of the primary webserver
            bool usingTls = false; //TLS is not yet supported
          
            string hostname = Dns.GetHostName();

            //Server id is just dns name for now
            string nodeId = $"{hostname}:{ServerPort}";

            Uri connectEp = BuildUri(usingTls, hostname, ServerPort, ConnectPath);
            Uri? discoveryEp = null;
         
            
            conf.WithCacheEndpoint(connectEp)
                .WithNodeId(nodeId)
                .WithTls(usingTls);

            //Get the discovery path (optional)
            if (!string.IsNullOrWhiteSpace(DiscoveryPath))
            {
                //Build the discovery endpoint, it must be an absolute uri
                discoveryEp = BuildUri(usingTls, hostname, ServerPort, DiscoveryPath);
                conf.EnableAdvertisment(discoveryEp);
            }

            //print the cluster configuration to the log
            plugin.Log.Information(CacheConfigTemplate,
                nodeId,
                usingTls,
                VerifyIp,
                WellKnownPath,
                connectEp,
                discoveryEp,
                DiscoveryInterval,
                MaxPeerConnections,
                MaxQueueDepth,
                EventQueuePurgeInterval
            );

            return conf;
        }

        private static Uri BuildUri(bool tls, string host, ushort port, string path)
        {
            return new UriBuilder
            {
                Scheme = tls ? "https" : "http",
                Host = host,
                Port = port,
                Path = path
            }.Uri;
        }
    }
}
