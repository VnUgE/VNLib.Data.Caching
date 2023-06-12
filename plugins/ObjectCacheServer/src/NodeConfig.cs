/*
* Copyright (c) 2023 Vaughn Nugent
* 
* Library: VNLib
* Package: ObjectCacheServer
* File: ObjectCacheServerEntry.cs 
*
* ObjectCacheServerEntry.cs is part of ObjectCacheServer which is part of the larger 
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
using System.Net.Http;
using System.Text.Json;
using System.Threading;
using System.Net.Sockets;
using System.Threading.Tasks;
using System.Collections.Generic;
using System.Security.Cryptography;

using VNLib.Plugins;
using VNLib.Utils;
using VNLib.Utils.Logging;
using VNLib.Utils.Extensions;
using VNLib.Hashing;
using VNLib.Hashing.IdentityUtility;
using VNLib.Data.Caching.Extensions;
using VNLib.Plugins.Extensions.Loading;


namespace VNLib.Data.Caching.ObjectCache.Server
{
    [ConfigurationName("cluster")]
    internal sealed class NodeConfig : VnDisposeable, IAsyncConfigurable, IAsyncBackgroundWork, IBrokerHeartbeatNotifier
    {
        const string CacheConfigTemplate = 
@"
    Cluster Configuration:
        Broker Address: {ba}
        Heartbeat Timeout: {hb}
        Node Id: {id}
        TlsEndabled: {tls},
        Cache Endpoint: {ep}
";

        public CacheNodeConfiguration Config { get; }
        public CacheAuthKeyStore KeyStore { get; }

        private readonly ManualResetEventSlim hearbeatHandle;
        private readonly TimeSpan _hearbeatTimeout;

        private string? _authToken;

        public NodeConfig(PluginBase plugin, IConfigScope config)
        {
            //Server id is just dns name for now
            string nodeId = Dns.GetHostName();
            Config = new();

            //Get the heartbeat interval
            TimeSpan heartBeatDelayMs = config["heartbeat_timeout_sec"].GetTimeSpan(TimeParseType.Seconds);

            string brokerAddr = config["broker_address"].GetString() ?? throw new KeyNotFoundException("Missing required key 'broker_address' for config 'cluster'");

            //Get the port of the primary webserver
            int port;
            bool usingTls;
            {
                JsonElement firstHost = plugin.HostConfig.GetProperty("virtual_hosts").EnumerateArray().First();

                port = firstHost.GetProperty("interface")
                        .GetProperty("port")
                        .GetInt32();

                //If the ssl element is present, ssl is enabled for the server
                usingTls = firstHost.TryGetProperty("ssl", out _);
            }

            //Get the cache endpoint config
            IConfigScope cacheEpConfig = plugin.GetConfigForType<ConnectEndpoint>();

            //The endpoint to advertise to cache clients that allows cache connections
            UriBuilder endpoint = new(usingTls ? Uri.UriSchemeHttps : Uri.UriSchemeHttp, nodeId, port, cacheEpConfig["path"].GetString());

            //Setup cache node config
            Config.WithCacheEndpoint(endpoint.Uri)
                    .WithNodeId(nodeId)
                    .WithTls(usingTls)
                    .WithBroker(new(brokerAddr));

            //Check if advertising is enabled
            if(config.TryGetValue("advertise", out JsonElement adEl) && adEl.GetBoolean())
            {
                Config.EnableAdvertisment(true, "");
            }

            //Init key store
            KeyStore = new(plugin);

            //Init heartbeat handle unsiganled waiting for first heartbeat
            hearbeatHandle = new(false);

            //Schedule heartbeat
            _ = plugin.ObserveWork(this, 500);

            //log the config
            plugin.Log.Information(CacheConfigTemplate,
                brokerAddr,
                heartBeatDelayMs,
                nodeId,
                usingTls,
                endpoint.Uri);
        }

        async Task IAsyncConfigurable.ConfigureServiceAsync(PluginBase plugin)
        {
            //Get cache private key for signing from the key store
            ReadOnlyJsonWebKey signingKey = await KeyStore.GetCachePrivateAsync();

            Config.WithSigningKey(signingKey);

            //Get broker public key for verifying from the key store
            ReadOnlyJsonWebKey brokerKey = await KeyStore.GetBrokerPublicAsync();

            Config.WithBrokerVerificationKey(brokerKey);
        }

        protected override void Free()
        {
            //Dispose the heartbeat handle
            hearbeatHandle.Dispose();

            //cleanup keys
            Config.SigningKey?.Dispose();
            Config.VerificationKey?.Dispose();
            Config.BrokerVerificationKey?.Dispose();
        }

        ///<inheritdoc/>
        public void HearbeatReceived()
        {
            //Set the heartbeat handle as received
            hearbeatHandle.Set();
        }

        ///<inheritdoc/>
        public string? GetAuthToken() => _authToken;

        ///<inheritdoc/>
        public Uri GetBrokerAddress() => Config.DiscoveryEndpoint!;

        ///<inheritdoc/>
        public ReadOnlyJsonWebKey GetBrokerPublicKey() => Config.BrokerVerificationKey!;
       

        /*
         * Worker loop for registering with the broker and monitoring hearbeat requests
         */
        async Task IAsyncBackgroundWork.DoWorkAsync(ILogProvider pluginLog, CancellationToken exitToken)
        {
            //Listen in loop
            while (true)
            {
                try
                {
                    //Regen the auth token before registering
                    _authToken = RandomHash.GetRandomBase32(32);

                    pluginLog.Information("Registering with cache broker server with id {id}", Config.NodeId);

                    //Register with the broker and pass the current auth token
                    await Config.RegisterWithBrokerAsync(_authToken);

                    //Enter heartbeat loop
                    while (true)
                    {
                        //Wait for the heartbeat timeout
                        await Task.Delay(_hearbeatTimeout, exitToken);

                        //Confrim the hearbeat was received within the timeout period
                        if (!hearbeatHandle.IsSet)
                        {
                            //If the heartbeat handle is not set, the heartbeat was not received, reg-register
                            pluginLog.Information("Broker missed hearbeat request");

                            //not received, break out of the heartbeat loop to re-register
                            break;
                        }

                        //Reset the handle and continue the heartbeat loop
                        hearbeatHandle.Reset();
                    }

                    //Add random delay to prevent all nodes from re-registering at the same time
                    await Task.Delay(RandomNumberGenerator.GetInt32(1000, 5000), exitToken);
                }
                catch (OperationCanceledException)
                {
                    pluginLog.Debug("Registration loop exited on unload");
                    break;
                }
                catch (TimeoutException)
                {
                    pluginLog.Warn("Failed to connect to cache broker server within the specified timeout period");
                }
                catch (HttpRequestException re) when (re.InnerException is SocketException)
                {
                    pluginLog.Warn("Cache broker is unavailable or network is unavailable");
                }
                catch(HttpRequestException re) when (re.StatusCode.HasValue)
                {
                    pluginLog.Warn("Failed to register with cache broker server, received status code {code}", re.StatusCode);
                }
                catch (Exception ex)
                {
                    pluginLog.Warn("Exception occured in registraion loop: {ex}", ex!.Message);
                }
            }
        }
        
    }
}
