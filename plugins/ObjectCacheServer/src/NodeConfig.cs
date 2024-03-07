/*
* Copyright (c) 2024 Vaughn Nugent
* 
* Library: VNLib
* Package: ObjectCacheServer
* File: NodeConfig.cs 
*
* NodeConfig.cs is part of ObjectCacheServer which is part of the larger 
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
using System.Collections.Generic;

using VNLib.Plugins;
using VNLib.Utils.Logging;
using VNLib.Utils.Extensions;
using VNLib.Plugins.Extensions.Loading;
using VNLib.Data.Caching.Extensions.Clustering;


namespace VNLib.Data.Caching.ObjectCache.Server
{
    [ConfigurationName("cluster")]
    internal sealed class NodeConfig 
    {
        //Default path for the well known endpoint
        const string DefaultPath = "/.well-known/vncache";

        public CacheNodeConfiguration Config { get; }

        public CacheAuthKeyStore KeyStore { get; }

        public TimeSpan DiscoveryInterval { get; }

        public TimeSpan EventQueuePurgeInterval { get; }

        public int MaxQueueDepth { get; }

        public string? DiscoveryPath { get; }

        public string ConnectPath { get; }

        public string WellKnownPath { get; }

        public bool VerifyIp { get; }

        /// <summary>
        /// The maximum number of peer connections to allow
        /// </summary>
        public uint MaxPeerConnections { get; } = 10;

        /// <summary>
        /// The maxium number of concurrent client connections to allow
        /// before rejecting new connections
        /// </summary>
        public uint MaxConcurrentConnections { get; }

        public NodeConfig(PluginBase plugin, IConfigScope config)
        { 
            //Get the port of the primary webserver
            int port;
            bool usingTls;
            {
                //Get the port number of the first virtual host
                JsonElement firstHost = plugin.HostConfig.GetProperty("virtual_hosts")
                                            .EnumerateArray()
                                            .First();

                port = firstHost.GetProperty("interface")
                        .GetProperty("port")
                        .GetInt32();

                //If the ssl element is present, ssl is enabled for the server
                usingTls = firstHost.TryGetProperty("ssl", out _);
            }
            string hostname = Dns.GetHostName();

            //Server id is just dns name for now
            string nodeId = $"{hostname}:{port}";
           
            //Init key store
            KeyStore = new(plugin);

            DiscoveryInterval = config.GetRequiredProperty("discovery_interval_sec", p => p.GetTimeSpan(TimeParseType.Seconds));
            EventQueuePurgeInterval = config.GetRequiredProperty("queue_purge_interval_sec", p => p.GetTimeSpan(TimeParseType.Seconds));           
            MaxQueueDepth = (int)config.GetRequiredProperty("max_queue_depth", p => p.GetUInt32());          
            ConnectPath = config.GetRequiredProperty("connect_path", p => p.GetString()!);           
            VerifyIp = config.GetRequiredProperty("verify_ip", p => p.GetBoolean());
            WellKnownPath = config.GetValueOrDefault("well_known_path", p => p.GetString()!, DefaultPath);
            MaxPeerConnections = config.GetValueOrDefault("max_peers", p => p.GetUInt32(), 10u);

            Uri connectEp = BuildUri(usingTls, hostname, port, ConnectPath);
            Uri? discoveryEp = null;

            //Setup cache node config
            (Config = new())
                .WithCacheEndpoint(connectEp)
                .WithNodeId(nodeId)
                .WithAuthenticator(KeyStore)
                .WithTls(usingTls);

            //Get the discovery path (optional)
            if (config.TryGetValue("discovery_path", out JsonElement discoveryPathEl))
            {
                DiscoveryPath = discoveryPathEl.GetString();

                //Enable advertisment if a discovery path is present
                if (!string.IsNullOrEmpty(DiscoveryPath))
                {
                    //Build the discovery endpoint, it must be an absolute uri
                    discoveryEp = BuildUri(usingTls, hostname, port, DiscoveryPath);
                    Config.EnableAdvertisment(discoveryEp);
                }
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

            //log the config
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
        }

        private static Uri BuildUri(bool tls, string host, int port, string path)
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
