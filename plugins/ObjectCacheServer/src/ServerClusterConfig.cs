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
using System.Collections.Generic;

using VNLib.Plugins;
using VNLib.Utils.Logging;
using VNLib.Utils.Extensions;
using VNLib.Plugins.Extensions.Loading;
using VNLib.Data.Caching.Extensions.Clustering;

namespace VNLib.Data.Caching.ObjectCache.Server
{
    [ConfigurationName("cluster")]
    internal sealed class ServerClusterConfig(PluginBase plugin, IConfigScope config)
    {
        public TimeSpan DiscoveryInterval { get; } = config.GetRequiredProperty("discovery_interval_sec", p => p.GetTimeSpan(TimeParseType.Seconds));

        public TimeSpan EventQueuePurgeInterval { get; } = config.GetRequiredProperty("queue_purge_interval_sec", p => p.GetTimeSpan(TimeParseType.Seconds));

        public int MaxQueueDepth { get; } = (int)config.GetRequiredProperty("max_queue_depth", p => p.GetUInt32());

        public string? DiscoveryPath { get; } = config.GetValueOrDefault("discovery_path", p => p.GetString(), null);

        public string ConnectPath { get; } = config.GetRequiredProperty("connect_path", p => p.GetString()!);

        public string WellKnownPath { get; } = config.GetValueOrDefault("well_known_path", CacheConstants.DefaultWellKnownPath) 
            ?? CacheConstants.DefaultWellKnownPath;

        public bool VerifyIp { get; } = config.GetValueOrDefault("verify_ip", true);

        /// <summary>
        /// The maximum number of peer connections to allow
        /// </summary>
        public uint MaxPeerConnections { get; } = config.GetValueOrDefault("max_peers", 10u);


        public ushort ServerPort { get; } = config.GetRequiredProperty("local_port", p => p.GetUInt16());

        /// <summary>
        /// The maxium number of concurrent client connections to allow
        /// before rejecting new connections
        /// </summary>
        public uint MaxConcurrentConnections { get; } = config.GetValueOrDefault("max_concurrent_connections", p => p.GetUInt32(), 100u);

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
