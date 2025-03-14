﻿/*
* Copyright (c) 2025 Vaughn Nugent
* 
* Library: VNLib
* Package: VNLib.Data.Caching.Providers.VNCache
* File: FBMCacheClient.cs 
*
* FBMCacheClient.cs is part of VNLib.Data.Caching.Providers.VNCache which is part of the larger 
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
using System.IO;
using System.Net.Http;
using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;
using System.Net.Sockets;
using System.Net.WebSockets;
using System.Collections.Generic;
using System.Security.Cryptography;

using VNLib.Hashing;
using VNLib.Hashing.IdentityUtility;
using VNLib.Utils.Memory;
using VNLib.Utils.Logging;
using VNLib.Net.Messaging.FBM.Client;
using VNLib.Data.Caching.Extensions;
using VNLib.Data.Caching.Extensions.Clustering;
using VNLib.Plugins;
using VNLib.Plugins.Extensions.Loading;
using VNLib.Plugins.Extensions.Loading.Events;

using VNLib.Data.Caching.Providers.VNCache.Clustering;

namespace VNLib.Data.Caching.Providers.VNCache.Internal
{

    /// <summary>
    /// A base class that manages 
    /// </summary>
    internal sealed class FBMCacheClient : VNCacheBase
    {
        private const string LOG_NAME = "FBMCache";
        private static readonly TimeSpan InitialDelay = TimeSpan.FromSeconds(10);
        private static readonly TimeSpan NoNodeDelay = TimeSpan.FromSeconds(10);

        private readonly VnCacheClientConfig _config;

        /*
         * This client may or may not be called in a plugin context.
         * We need to support both. There are extra features we can 
         * take advantage of if runnin in a plugin context
         */
        private readonly (PluginBase, ILogProvider)? _plugin;

        private bool _isConnected;
        private FBMClient? _client;

        /// <summary>
        /// The internal heap used for FBMClients
        /// </summary>
        public IUnmangedHeap BufferHeap { get; } = MemoryUtil.Shared;

        /// <summary>
        /// Gets a value that determines if the client is currently connected to a server
        /// </summary>
        public override bool IsConnected => _isConnected;

        internal FBMCacheClient(PluginBase plugin, VnCacheClientConfig config) : this(config)
        {
            _plugin = (plugin, plugin.Log.CreateScope(LOG_NAME));

            /*
            * When running in plugin context, the user may have specified a custom 
            * serializer assembly to load. If so, we need to load the assembly and
            * get the serializer instances.
            */

            if (!string.IsNullOrWhiteSpace(_config.SerializerDllPath))
            {
                //Load the custom serializer assembly and get the serializer and deserializer instances
                _config.CacheObjectSerializer = plugin.CreateServiceExternal<ICacheObjectSerializer>(_config.SerializerDllPath);

                //Avoid creating another instance if the deserializer is the same as the serializer
                if (_config.CacheObjectSerializer is ICacheObjectDeserializer cod)
                {
                    _config.CacheObjectDeserializer = cod;
                }
                else
                {
                    _config.CacheObjectDeserializer = plugin.CreateServiceExternal<ICacheObjectDeserializer>(_config.SerializerDllPath);
                }
            }
        }

        internal FBMCacheClient(VnCacheClientConfig config) : base(config)
        {
            Debug.Assert(config != null);
            _config = config;
        }


        private void ConfigureCluster(ref IClusterNodeIndex index, ref VNCacheClusterClient cluster)
        {
            //Init the client with default settings
            FBMClientConfig conf = FBMDataCacheExtensions.GetDefaultConfig(
                BufferHeap,
                maxMessageSize: (int)_config.MaxBlobSize,
                timeout: _config.RequestTimeout,
                debugLog: _config.ClientDebugLog
            );

            FBMClientFactory clientFactory = new(
                in conf,
                webSocketManager: new FBMFallbackClientWsFactory(),
                maxClients: 10
            );

            CacheClientConfiguration clusterConfig = new CacheClientConfiguration()
                .WithTls(_config.UseTls)
                .WithInitialPeers(_config.GetInitialNodeUris());

            //See if were executing in the context of a plugin
            if (_plugin.HasValue)
            {
                (PluginBase plugin, ILogProvider scoped) = _plugin.Value;

                //When in plugin context, we can use plugin local secrets and a log-based error handler
                clusterConfig
                    .WithAuthenticator(new AuthManager(plugin))
                    .WithErrorHandler(new DiscoveryErrHAndler(scoped));
            }

            /*
             * If the user did not assign serializers, or it was not loaded from an 
             * external assembly in a plugin context, the serializers may be assigned 
             * null. So this ensures seralizer instances are defined before running.
             */
            _config.CacheObjectSerializer ??= new JsonCacheObjectSerializer(256);
            _config.CacheObjectDeserializer ??= new JsonCacheObjectSerializer(256);

            cluster = clusterConfig.ToClusterClient(clientFactory);

            index = ClusterNodeIndex.CreateIndex(cluster);
        }


        /// <summary>
        /// Begins the lifecycle of a cache cluster client by discovering cluster nodes
        /// choosing a node, and running a connection loop with the cluster. 
        /// <para>
        /// Unless running in a plugin context, you must call this function to begin the
        /// cache client. DO NOT call this function if running in plugin context, it will
        /// be scheduled in the background.
        /// </para>
        /// <para>
        /// This function will not exit unless an unrecoverable error occurs, 
        /// or the exit token is cancelled. You should always provide a cancellation
        /// token to this function to allow for graceful shutdown.
        /// </para>
        /// </summary>
        /// <param name="operationLog">A log provider to write connection and logging data to</param>
        /// <param name="exitToken">A token that will gracefully stop a client connection when cancelled</param>
        /// <returns>A task that represents this background operation</returns>
        public override async Task RunAsync(ILogProvider operationLog, CancellationToken exitToken)
        {
            IClusterNodeIndex index = null!;
            VNCacheClusterClient cluster = null!;

            ConfigureCluster(ref index, ref cluster);

            //Set a default node delay if null
            TimeSpan initNodeDelay = _config.InitialNodeDelay.HasValue
                ? TimeSpan.FromSeconds(_config.InitialNodeDelay.Value)
                : InitialDelay;

            if (_plugin.HasValue && index is IIntervalScheduleable mstr)
            {
                (PluginBase plugin, ILogProvider scoped) = _plugin.Value;

                //Schedule discovery interval
                plugin.ScheduleInterval(mstr, _config.DiscoveryInterval);

                //Run discovery after initial delay if interval is greater than initial delay
                if (_config.DiscoveryInterval > initNodeDelay)
                {
                    //Run a manual initial load
                    scoped.Information("Running initial discovery in {delay}", initNodeDelay);
                    _ = plugin.ObserveWork(() => mstr.OnIntervalAsync(scoped, plugin.UnloadToken), (int)initNodeDelay.TotalMilliseconds);
                }
            }

            try
            {
                //Initial delay
                operationLog.Debug("Worker started, waiting for startup delay");
                await Task.Delay(initNodeDelay, exitToken);

                CacheNodeAdvertisment? node = null;

                while (true)
                {
                    /*
                     * The cache node index is shared across plugin boundries. If the current
                     * instance is holding the master index, it will be scheduleable, and 
                     * can be manually invoked if no nodes are found
                     */
                    if (index is IIntervalScheduleable sch)
                    {
                        try
                        {
                            //Wait for a discovery to complete  
                            await index.WaitForDiscoveryAsync(exitToken);
                        }
                        catch (CacheDiscoveryFailureException cdfe)
                        {
                            operationLog.Error("Failed to discover nodes, will try again\n{err}", cdfe.Message);
                            //Continue
                        }

                        //Get the next node to connect to
                        node = index.GetNextNode();

                        if (node is null)
                        {
                            operationLog.Warn("No nodes available to connect to, trying again in {delay}", NoNodeDelay);
                            await Task.Delay(NoNodeDelay, exitToken);

                            //Run another manual discovery if the interval is greater than the delay
                            if (_config.DiscoveryInterval > NoNodeDelay)
                            {
                                operationLog.Debug("Forcing a manual discovery");

                                //We dont need to await this because it is awaited at the top of the loop
                                _ = sch.OnIntervalAsync(operationLog, exitToken);
                            }

                            continue;
                        }
                    }
                    else
                    {
                        try
                        {
                            //Wait for a discovery to complete  
                            await index.WaitForDiscoveryAsync(exitToken);
                        }
                        catch (Exception ex)
                        {
                            operationLog.Debug("Failed to wait for discovery\n{err}", ex.Message);
                            //Exception types from the other side so we can't really granually handle
                            //them, but master instance should so we just need to wait
                        }

                        //Get the next node to connect to
                        node = index.GetNextNode();

                        //Again master instance will handle this condition, we just need to wait
                        if (node is null)
                        {
                            await Task.Delay(NoNodeDelay, exitToken);
                            continue;
                        }
                    }

                    //Ready to connect

                    try
                    {
                        operationLog.Debug("Connecting to {node}", node);

                        //Connect to the node and save new client
                        _client = await cluster.ConnectToCacheAsync(node, exitToken);

                        if (operationLog.IsEnabled(LogLevel.Debug))
                        {
                            operationLog.Debug("Connected server: {s}", node);
                        }
                        else
                        {
                            operationLog.Information("Successfully connected to cache node");
                        }

                        //Set connection status flag
                        _isConnected = true;

                        //Wait for disconnect
                        await _client.WaitForExitAsync(exitToken);

                        operationLog.Information("Cache server disconnected");
                    }
                    catch (TimeoutException)
                    {
                        operationLog.Warn("Failed to establish a websocket connection to cache server within the timeout period");
                    }
                    catch (WebSocketException wse)
                    {
                        operationLog.Warn("Failed to establish a websocket connection to cache server {reason}", wse.Message);
                        operationLog.Verbose("Stack trace: {re}", wse);
                    }
                    //SEs may be raised when the server is not available
                    catch (HttpRequestException he) when (he.InnerException is SocketException se)
                    {
                        operationLog.Debug("Failed to connect to random cache server because a TCP connection could not be established");
                        operationLog.Verbose("Stack trace: {re}", se);
                        await Task.Delay(1000, exitToken);
                    }
                    catch (HttpRequestException he) when (he.InnerException is IOException ioe && ioe.InnerException is SocketException se)
                    {
                        operationLog.Debug("Failed to connect to random cache server because a TCP connection could not be established");
                        operationLog.Verbose("Stack trace: {re}", se);
                        await Task.Delay(1000, exitToken);
                    }
                    catch (HttpRequestException he) when (he.StatusCode.HasValue)
                    {
                        operationLog.Warn("Failed to negotiate with cache server {reason}", he.Message);
                        operationLog.Verbose("Stack trace: {re}", he);
                        await Task.Delay(1000, exitToken);
                    }
                    finally
                    {
                        _isConnected = false;

                        //Cleanup client
                        _client?.Dispose();
                    }

                    //Loop again
                }
            }
            catch (OperationCanceledException)
            {
                //Normal exit from listening loop
            }
            catch (FBMServerNegiationException fne)
            {
                operationLog.Error("Failed to negotiate connection with cache server. Please check your configuration\n {reason}", fne.Message);
            }
            catch (Exception ex)
            {
                operationLog.Error(ex, "Unhandled exception occured in background cache client listening task");
            }

            operationLog.Information("Cache client exited");
        }


        ///<inheritdoc/>
        public override Task<bool> DeleteAsync(string key, CancellationToken cancellation)
        {
            return !IsConnected
              ? Task.FromException<bool>(new InvalidOperationException("The underlying client is not connected to a cache node"))
              : _client!.DeleteObjectAsync(key, cancellation);
        }

        ///<inheritdoc/>
        public override Task<T> GetAsync<T>(string key, ICacheObjectDeserializer deserializer, CancellationToken cancellation)
        {
            return !IsConnected
            ? Task.FromException<T>(new InvalidOperationException("The underlying client is not connected to a cache node"))
            : _client!.GetObjectAsync<T>(key, deserializer, cancellation);
        }

        ///<inheritdoc/>
        public override Task AddOrUpdateAsync<T>(string key, string? newKey, T value, ICacheObjectSerializer serialzer, CancellationToken cancellation)
        {
            return !IsConnected
            ? Task.FromException(new InvalidOperationException("The underlying client is not connected to a cache node"))
            : _client!.AddOrUpdateObjectAsync(key, newKey, value, serialzer, cancellation);
        }

        ///<inheritdoc/>
        public override Task GetAsync<T>(string key, ObjectDataSet<T> callback, T state, CancellationToken cancellation)
        {
            return !IsConnected
            ? Task.FromException(new InvalidOperationException("The underlying client is not connected to a cache node"))
            : _client!.GetObjectAsync(key, callback, state, cancellation);
        }

        ///<inheritdoc/>
        public override Task AddOrUpdateAsync<T>(string key, string? newKey, ObjectDataGet<T> callback, T state, CancellationToken cancellation)
        {
            return !IsConnected
            ? Task.FromException(new InvalidOperationException("The underlying client is not connected to a cache node"))
            : _client!.AddOrUpdateObjectAsync(key, newKey, callback, state, cancellation);
        }

        ///<inheritdoc/>
        public override object GetUnderlyingStore() => _client ?? throw new InvalidOperationException("The client is not currently connected");

        private sealed class AuthManager(PluginBase plugin) : ICacheAuthManager
        {

            private IAsyncLazy<ReadOnlyJsonWebKey> _sigKey = plugin.GetSecretAsync("client_private_key").ToLazy(static r => r.GetJsonWebKey());
            private IAsyncLazy<ReadOnlyJsonWebKey> _verKey = plugin.GetSecretAsync("cache_public_key").ToLazy(static r => r.GetJsonWebKey());

            public async Task AwaitLazyKeyLoad()
            {
                await _sigKey;
                await _verKey;
            }

            ///<inheritdoc/>
            public IReadOnlyDictionary<string, string?> GetJwtHeader()
            {
                //Get the signing key jwt header
                return _sigKey.Value.JwtHeader;
            }

            ///<inheritdoc/>
            public void SignJwt(JsonWebToken jwt)
            {
                //Sign the jwt with signing key
                jwt.SignFromJwk(_sigKey.Value);
            }

            ///<inheritdoc/>
            public byte[] SignMessageHash(byte[] hash, HashAlg alg)
            {
                //try to get the rsa alg for the signing key
                using RSA? rsa = _sigKey.Value.GetRSAPrivateKey();
                if (rsa != null)
                {
                    return rsa.SignHash(hash, alg.GetAlgName(), RSASignaturePadding.Pkcs1);
                }

                //try to get the ecdsa alg for the signing key
                using ECDsa? ecdsa = _sigKey.Value.GetECDsaPrivateKey();
                if (ecdsa != null)
                {
                    return ecdsa.SignHash(hash);
                }

                throw new NotSupportedException("The signing key is not a valid RSA or ECDSA key");
            }

            ///<inheritdoc/>
            public bool VerifyJwt(JsonWebToken jwt, bool isPeer)
            {
                return jwt.VerifyFromJwk(_verKey.Value);
            }

            ///<inheritdoc/>
            public bool VerifyMessageHash(ReadOnlySpan<byte> hash, HashAlg alg, ReadOnlySpan<byte> signature, bool isPeer)
            {
                //try to get the rsa alg for the signing key
                using RSA? rsa = _verKey.Value.GetRSAPublicKey();
                if (rsa != null)
                {
                    return rsa.VerifyHash(hash, signature, alg.GetAlgName(), RSASignaturePadding.Pkcs1);
                }

                //try to get the ecdsa alg for the signing key
                using ECDsa? ecdsa = _verKey.Value.GetECDsaPublicKey();
                if (ecdsa != null)
                {
                    return ecdsa.VerifyHash(hash, signature);
                }

                throw new NotSupportedException("The current key is not an RSA or ECDSA key and is not supported");
            }
        }

        private sealed record class DiscoveryErrHAndler(ILogProvider Logger) : ICacheDiscoveryErrorHandler
        {
            public void OnDiscoveryError(CacheNodeAdvertisment errorNode, Exception ex)
                => OnDiscoveryError(ex, errorNode, address: null);

            public void OnDiscoveryError(Uri errorAddress, Exception ex)
                => OnDiscoveryError(ex, errorNode: null, errorAddress);

            public void OnDiscoveryError(Exception ex, CacheNodeAdvertisment? errorNode, Uri? address)
            {
                string node = errorNode?.NodeId ?? address?.ToString() ?? "unknown";

                if (ex is HttpRequestException he)
                {
                    if (he.InnerException is SocketException se)
                    {
                        LogErrorException(se);
                        return;
                    }

                    LogErrorException(he);
                    return;
                }

                LogErrorException(ex);
                return;

                void LogErrorException(Exception ex)
                {
                    if (Logger.IsEnabled(LogLevel.Debug))
                    {
                        Logger.Error("Failed to discover nodes from server {s} cause:\n{err}", node, ex);
                    }
                    else
                    {
                        Logger.Error("Failed to discover nodes from server {s} cause:\n{err}", node, ex.Message);
                    }
                }
            }
        }
    }
}