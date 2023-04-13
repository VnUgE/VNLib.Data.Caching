/*
* Copyright (c) 2023 Vaughn Nugent
* 
* Library: VNLib
* Package: VNLib.Plugins.Extensions.VNCache
* File: VnCacheClient.cs 
*
* VnCacheClient.cs is part of VNLib.Plugins.Extensions.VNCache which is part of the larger 
* VNLib collection of libraries and utilities.
*
* VNLib.Plugins.Extensions.VNCache is free software: you can redistribute it and/or modify 
* it under the terms of the GNU Affero General Public License as 
* published by the Free Software Foundation, either version 3 of the
* License, or (at your option) any later version.
*
* VNLib.Plugins.Extensions.VNCache is distributed in the hope that it will be useful,
* but WITHOUT ANY WARRANTY; without even the implied warranty of
* MERCHANTABILITY or FITNESS FOR A PARTICULAR PURPOSE. See the
* GNU Affero General Public License for more details.
*
* You should have received a copy of the GNU Affero General Public License
* along with this program.  If not, see https://www.gnu.org/licenses/.
*/

using System;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using System.Net.Sockets;
using System.Net.WebSockets;
using System.Collections.Generic;
using System.Security.Cryptography;

using VNLib.Utils.Memory;
using VNLib.Utils.Logging;
using VNLib.Hashing.IdentityUtility;
using VNLib.Data.Caching;
using VNLib.Data.Caching.Extensions;
using VNLib.Data.Caching.ObjectCache;
using VNLib.Net.Messaging.FBM.Client;
using VNLib.Plugins.Extensions.Loading;

namespace VNLib.Plugins.Extensions.VNCache
{
    public interface ICacheRefreshPolicy
    {
        TimeSpan MaxCacheAge { get; }

        TimeSpan RefreshInterval { get; }
    }

    /// <summary>
    /// A base class that manages 
    /// </summary>
    [ConfigurationName(VNCacheExtensions.CACHE_CONFIG_KEY)]
    internal class VnCacheClient : IGlobalCacheProvider, IAsyncBackgroundWork, IAsyncConfigurable
    {
        private readonly VnCacheClientConfig _config;

        /// <summary>
        /// The internal client
        /// </summary>
        public FBMClient Client { get; }

        /// <summary>
        /// Gets a value that determines if the client is currently connected to a server
        /// </summary>
        public bool IsConnected { get; private set; }

        public VnCacheClient(PluginBase pbase, IConfigScope config)
            :this(
                 config.Deserialze<VnCacheClientConfig>(), 
                 pbase.IsDebug() ? pbase.Log : null
                )
        {}

        public VnCacheClient(VnCacheClientConfig config, ILogProvider? debugLog)
        {
            //Validate config
            (config as IOnConfigValidation).Validate();

            _config = config;

            Uri brokerUri = new(config.BrokerAddress!);

            //Init the client with default settings
            FBMClientConfig conf = FBMDataCacheExtensions.GetDefaultConfig(MemoryUtil.Shared, config.MaxMessageSize!.Value, config.RequestTimeout, debugLog);

            Client = new(conf);

            //Add the configuration to the client
            Client.GetCacheConfiguration()
                .WithBroker(brokerUri)
                .WithTls(brokerUri.Scheme == Uri.UriSchemeHttps);
        }
       

        public virtual async Task ConfigureServiceAsync(PluginBase plugin)
        {
            //Get keys async
            Task<ReadOnlyJsonWebKey?> clientPrivTask = plugin.TryGetSecretAsync("client_private_key").ToJsonWebKey();
            Task<ReadOnlyJsonWebKey?> brokerPubTask = plugin.TryGetSecretAsync("broker_public_key").ToJsonWebKey();
            Task<ReadOnlyJsonWebKey?> cachePubTask = plugin.TryGetSecretAsync("cache_public_key").ToJsonWebKey();

            //Wait for all tasks to complete
            _ = await Task.WhenAll(clientPrivTask, brokerPubTask, cachePubTask);

            ReadOnlyJsonWebKey clientPriv = await clientPrivTask ?? throw new KeyNotFoundException("Missing required secret client_private_key");
            ReadOnlyJsonWebKey brokerPub = await brokerPubTask ?? throw new KeyNotFoundException("Missing required secret broker_public_key");
            ReadOnlyJsonWebKey cachePub = await cachePubTask ?? throw new KeyNotFoundException("Missing required secret cache_public_key");

            //Connection authentication methods
            Client.GetCacheConfiguration()
                .WithVerificationKey(cachePub)
                .WithSigningCertificate(clientPriv)
                .WithBrokerVerificationKey(brokerPub);
        }

        /*
         * Background work method manages the remote cache connection
         * to the cache cluster
         */
        public virtual async Task DoWorkAsync(ILogProvider pluginLog, CancellationToken exitToken)
        {
            try
            {      
                while (true)
                {
                    //Load the server list
                    ActiveServer[]? servers;
                    while (true)
                    {
                        try
                        {
                            pluginLog.Debug("Discovering cluster nodes in broker");
                            //Get server list
                            servers = await Client.DiscoverCacheNodesAsync(exitToken);
                            break;
                        }
                        catch (HttpRequestException re) when (re.InnerException is SocketException)
                        {
                            pluginLog.Warn("Broker server is unreachable");
                        }
                        catch (Exception ex)
                        {
                            pluginLog.Warn("Failed to get server list from broker, reason {r}", ex.Message);
                        }

                        //Gen random ms delay
                        int randomMsDelay = RandomNumberGenerator.GetInt32(1000, 2000);
                        await Task.Delay(randomMsDelay, exitToken);
                    }

                    if (servers?.Length == 0)
                    {
                        pluginLog.Warn("No cluster nodes found, retrying");
                        await Task.Delay(_config.RetryInterval, exitToken);
                        continue;
                    }

                    try
                    {
                        pluginLog.Debug("Connecting to random cache server");

                        //Connect to a random server
                        ActiveServer selected = await Client.ConnectToRandomCacheAsync(exitToken);
                        pluginLog.Debug("Connected to cache server {s}", selected.ServerId);

                        //Set connection status flag
                        IsConnected = true;

                        //Wait for disconnect
                        await Client.WaitForExitAsync(exitToken);

                        pluginLog.Debug("Cache server disconnected");
                    }
                    catch (WebSocketException wse)
                    {
                        pluginLog.Warn("Failed to connect to cache server {reason}", wse.Message);
                        continue;
                    }
                    catch (HttpRequestException he) when (he.InnerException is SocketException)
                    {
                        pluginLog.Debug("Failed to connect to random cache server server");
                        //Continue next loop
                        continue;
                    }
                    finally
                    {
                        IsConnected = false;
                    }
                }
            }
            catch (OperationCanceledException)
            {
                //Normal exit from listening loop
            }
            catch (KeyNotFoundException e)
            {
                pluginLog.Error("Missing required configuration variable for VnCache client: {0}", e.Message);
            }
            catch (FBMServerNegiationException fne)
            {
                pluginLog.Error("Failed to negotiate connection with cache server {reason}", fne.Message);
            }
            catch (Exception ex)
            {
                pluginLog.Error(ex, "Unhandled exception occured in background cache client listening task");
            }
            finally
            {
                //Dispose the client on exit
                Client.Dispose();
            }
            pluginLog.Information("Cache client exited");
        }


        ///<inheritdoc/>
        public virtual Task AddOrUpdateAsync<T>(string key, string? newKey, T value, CancellationToken cancellation)
        {
            return !IsConnected
               ? throw new InvalidOperationException("The underlying client is not connected to a cache node")
               : Client!.AddOrUpdateObjectAsync(key, newKey, value, cancellation);
        }

        ///<inheritdoc/>
        public virtual Task DeleteAsync(string key, CancellationToken cancellation)
        {
            return !IsConnected
              ? throw new InvalidOperationException("The underlying client is not connected to a cache node")
              : Client!.DeleteObjectAsync(key, cancellation);
        }

        ///<inheritdoc/>
        public virtual Task<T?> GetAsync<T>(string key, CancellationToken cancellation)
        {
            return !IsConnected
               ? throw new InvalidOperationException("The underlying client is not connected to a cache node")
               : Client!.GetObjectAsync<T>(key, cancellation);
        }

        ///<inheritdoc/>
        public virtual Task<T?> GetAsync<T>(string key, ICacheObjectDeserialzer deserializer, CancellationToken cancellation)
        {
            return !IsConnected
               ? throw new InvalidOperationException("The underlying client is not connected to a cache node")
               : Client!.GetObjectAsync<T>(key, deserializer, cancellation);
        }

        ///<inheritdoc/>
        public virtual Task AddOrUpdateAsync<T>(string key, string? newKey, T value, ICacheObjectSerialzer serialzer, CancellationToken cancellation)
        {
            return !IsConnected
             ? throw new InvalidOperationException("The underlying client is not connected to a cache node")
             : Client!.AddOrUpdateObjectAsync(key, newKey, value, serialzer, cancellation);
        }
    }
}