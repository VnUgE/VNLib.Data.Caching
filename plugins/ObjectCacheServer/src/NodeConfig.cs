/*
* Copyright (c) 2023 Vaughn Nugent
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

using VNLib.Plugins;
using VNLib.Utils.Logging;
using VNLib.Utils.Extensions;
using VNLib.Plugins.Extensions.Loading;
using VNLib.Data.Caching.ObjectCache.Server.Endpoints;
using VNLib.Data.Caching.Extensions.Clustering;

namespace VNLib.Data.Caching.ObjectCache.Server
{
    [ConfigurationName("cluster")]
    internal sealed class NodeConfig 
    {
        const string CacheConfigTemplate = 
@"
Cluster Configuration:
    Node Id: {id}
    TlsEndabled: {tls}
    Cache Endpoint: {ep}
    Discovery Endpoint: {dep}
    Discovery Interval: {di}
    Max Peers: {mpc}
";

        public CacheNodeConfiguration Config { get; }

        public CacheAuthKeyStore KeyStore { get; }

        public TimeSpan DiscoveryInterval { get; }

        /// <summary>
        /// The maximum number of peer connections to allow
        /// </summary>
        public uint MaxPeerConnections { get; } = 10;

        public NodeConfig(PluginBase plugin, IConfigScope config)
        {           

            Config = new();

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

            //The endpoint to advertise to cache clients that allows cache connections
            Uri cacheEndpoint = GetEndpointUri<ConnectEndpoint>(plugin, usingTls, port, hostname);

            //Init key store
            KeyStore = new(plugin);

            //Setup cache node config
            Config.WithCacheEndpoint(cacheEndpoint)
                    .WithNodeId(nodeId)
                    .WithAuthenticator(KeyStore)
                    .WithTls(usingTls);

            //Check if advertising is enabled
            if(plugin.HasConfigForType<PeerDiscoveryEndpoint>())
            {
                //Get the the broadcast endpoint
                Uri discoveryEndpoint = GetEndpointUri<PeerDiscoveryEndpoint>(plugin, usingTls, port, hostname);

                //Enable advertising
                Config.EnableAdvertisment(discoveryEndpoint);
            }


            DiscoveryInterval = config["discovery_interval_sec"].GetTimeSpan(TimeParseType.Seconds);

            //Get the max peer connections
            if(config.TryGetValue("max_peers", out JsonElement maxPeerEl))
            {
                MaxPeerConnections = maxPeerEl.GetUInt32();
            }

            //log the config
            plugin.Log.Information(CacheConfigTemplate,
                nodeId,
                usingTls,
                cacheEndpoint,
                Config.DiscoveryEndpoint,
                DiscoveryInterval,
                MaxPeerConnections
            );
        }

        private static Uri GetEndpointUri<T>(PluginBase plugin, bool usingTls, int port, string hostName) where T: IEndpoint
        {
            //Get the cache endpoint config
            IConfigScope cacheEpConfig = plugin.GetConfigForType<T>();

            //The endpoint to advertise to cache clients that allows cache connections
            return new UriBuilder(usingTls ? Uri.UriSchemeHttps : Uri.UriSchemeHttp, hostName, port, cacheEpConfig["path"].GetString()).Uri;
        }
    }
}
