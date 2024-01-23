/*
* Copyright (c) 2024 Vaughn Nugent
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
using System.Net.Http;
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

namespace VNLib.Data.Caching.Providers.VNCache
{

    /// <summary>
    /// A base class that manages 
    /// </summary>
    [ConfigurationName(VNCacheClient.CACHE_CONFIG_KEY)]
    internal sealed class FBMCacheClient : VNCacheBase, IAsyncBackgroundWork
    {
        private const string LOG_NAME = "CLIENT";
        private static readonly TimeSpan InitialDelay = TimeSpan.FromSeconds(10);
        private static readonly TimeSpan NoNodeDelay = TimeSpan.FromSeconds(10);

        private readonly VnCacheClientConfig _config;
        private readonly IClusterNodeIndex _index;
        private readonly FBMClientFactory _clientFactory;
        private readonly TimeSpan _initNodeDelay;

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

        public FBMCacheClient(PluginBase plugin, IConfigScope config)
        : this(
            config.Deserialze<VnCacheClientConfig>(),
            plugin.IsDebug() ? plugin.Log : null
        )
        {
            ILogProvider scoped = plugin.Log.CreateScope(LOG_NAME);

            //Set authenticator and error handler
            _clientFactory.GetCacheConfiguration()
                .WithAuthenticator(new AuthManager(plugin))
                .WithErrorHandler(new DiscoveryErrHAndler(scoped));

            //Only the master index is schedulable
            if (_index is IIntervalScheduleable sch)
            {
                //Schedule discovery interval
                plugin.ScheduleInterval(sch, _config.DiscoveryInterval);

                //Run discovery after initial delay if interval is greater than initial delay
                if (_config.DiscoveryInterval > _initNodeDelay)
                {
                    //Run a manual initial load
                    scoped.Information("Running initial discovery in {delay}", _initNodeDelay);
                    _ = plugin.ObserveWork(() => sch.OnIntervalAsync(scoped, plugin.UnloadToken), (int)_initNodeDelay.TotalMilliseconds);
                }
            }
        }

        public FBMCacheClient(VnCacheClientConfig config, ILogProvider? debugLog):base(config)
        {
            //Validate config
            (config as IOnConfigValidation).Validate();

            _config = config;

            //Set a default node delay if null
            _initNodeDelay = _config.InitialNodeDelay.HasValue ? TimeSpan.FromSeconds(_config.InitialNodeDelay.Value) : InitialDelay;

            //Init the client with default settings
            FBMClientConfig conf = FBMDataCacheExtensions.GetDefaultConfig(BufferHeap, (int)config.MaxBlobSize, config.RequestTimeout, debugLog);

            FBMFallbackClientWsFactory wsFactory = new();
            _clientFactory = new(in conf, wsFactory);

            //Add the configuration to the client
            _clientFactory.GetCacheConfiguration()
                .WithTls(config.UseTls)
                .WithInitialPeers(config.GetInitialNodeUris());

            //Init index
            _index = ClusterNodeIndex.CreateIndex(_clientFactory.GetCacheConfiguration());
        }

        /*
         * Background work method manages the remote cache connection
         * to the cache cluster
         */
        public async Task DoWorkAsync(ILogProvider pluginLog, CancellationToken exitToken)
        {
            //Scope log
            pluginLog = pluginLog.CreateScope(LOG_NAME);

            try
            {
                //Initial delay
                pluginLog.Debug("Worker started, waiting for startup delay");
                await Task.Delay(_initNodeDelay, exitToken);

                CacheNodeAdvertisment? node = null;

                while (true)
                {
                    /*
                     * The cache node index is shared across plugin boundries. If the current
                     * instance is holding the master index, it will be scheduleable, and 
                     * can be manually invoked if no nodes are found
                     */
                    if (_index is IIntervalScheduleable sch)
                    {
                        try
                        {
                            //Wait for a discovery to complete  
                            await _index.WaitForDiscoveryAsync(exitToken);
                        }
                        catch (CacheDiscoveryFailureException cdfe)
                        {
                            pluginLog.Error("Failed to discover nodes, will try again\n{err}", cdfe.Message);
                            //Continue
                        }

                        //Get the next node to connect to
                        node = _index.GetNextNode();

                        if (node is null)
                        {
                            pluginLog.Warn("No nodes available to connect to, trying again in {delay}", NoNodeDelay);
                            await Task.Delay(NoNodeDelay, exitToken);

                            //Run another manual discovery if the interval is greater than the delay
                            if (_config.DiscoveryInterval > NoNodeDelay)
                            {
                                pluginLog.Debug("Forcing a manual discovery");

                                //We dont need to await this because it is awaited at the top of the loop
                                _ = sch.OnIntervalAsync(pluginLog, exitToken);
                            }

                            continue;
                        }
                    }
                    else
                    {
                        try
                        {
                            //Wait for a discovery to complete  
                            await _index.WaitForDiscoveryAsync(exitToken);
                        }
                        catch (CacheDiscoveryFailureException)
                        {
                            //Ignore as master instance will handle this error
                        }

                        //Get the next node to connect to
                        node = _index.GetNextNode();

                        //Again master instance will handle this condition, we just need to wait
                        if(node is null)
                        {
                            await Task.Delay(NoNodeDelay, exitToken);
                            continue;
                        }
                    }

                    //Ready to connect

                    try
                    {
                        pluginLog.Debug("Connecting to {node}", node);

                        //Connect to the node and save new client
                        _client = await _clientFactory.ConnectToCacheAsync(node, exitToken);

                        if (pluginLog.IsEnabled(LogLevel.Debug))
                        {
                            pluginLog.Debug("Connected server: {s}", node);
                        }
                        else
                        {
                            pluginLog.Information("Successfully connected to cache node");
                        }

                        //Set connection status flag
                        _isConnected = true;

                        //Wait for disconnect
                        await _client.WaitForExitAsync(exitToken);

                        pluginLog.Information("Cache server disconnected");
                    }
                    catch (TimeoutException)
                    {
                        pluginLog.Warn("Failed to establish a websocket connection to cache server within the timeout period");
                    }
                    catch (WebSocketException wse)
                    {
                        pluginLog.Warn("Failed to establish a websocket connection to cache server {reason}", wse.Message);
                        pluginLog.Verbose("Stack trace: {re}", wse);
                    }
                    //SEs may be raised when the server is not available
                    catch (HttpRequestException he) when (he.InnerException is SocketException)
                    {
                        pluginLog.Debug("Failed to connect to random cache server because a TCP connection could not be established");
                        pluginLog.Verbose("Stack trace: {re}", he.InnerException);
                    }
                    catch (HttpRequestException he) when (he.StatusCode.HasValue)
                    {
                        pluginLog.Warn("Failed to negotiate with cache server {reason}", he.Message);
                        pluginLog.Verbose("Stack trace: {re}", he);
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
                pluginLog.Error("Failed to negotiate connection with cache server. Please check your configuration\n {reason}", fne.Message);
            }
            catch (Exception ex)
            {
                pluginLog.Error(ex, "Unhandled exception occured in background cache client listening task");
            }

            pluginLog.Information("Cache client exited");
        }


        ///<inheritdoc/>
        public override Task<bool> DeleteAsync(string key, CancellationToken cancellation)
        {
            return !IsConnected
              ? throw new InvalidOperationException("The underlying client is not connected to a cache node")
              : _client!.DeleteObjectAsync(key, cancellation);
        }

        ///<inheritdoc/>
        public override Task<T> GetAsync<T>(string key, ICacheObjectDeserializer deserializer, CancellationToken cancellation)
        {
            return !IsConnected
            ? throw new InvalidOperationException("The underlying client is not connected to a cache node")
            : _client!.GetObjectAsync<T>(key, deserializer, cancellation);
        }

        ///<inheritdoc/>
        public override Task AddOrUpdateAsync<T>(string key, string? newKey, T value, ICacheObjectSerializer serialzer, CancellationToken cancellation)
        {
            return !IsConnected
            ? throw new InvalidOperationException("The underlying client is not connected to a cache node")
            : _client!.AddOrUpdateObjectAsync(key, newKey, value, serialzer, cancellation);
        }

        ///<inheritdoc/>
        public override Task GetAsync<T>(string key, ObjectDataSet<T> callback, T state, CancellationToken cancellation)
        {
            return !IsConnected
            ? throw new InvalidOperationException("The underlying client is not connected to a cache node")
            : _client!.GetObjectAsync(key, callback, state, cancellation);
        }

        ///<inheritdoc/>
        public override Task AddOrUpdateAsync<T>(string key, string? newKey, ObjectDataGet<T> callback, T state, CancellationToken cancellation)
        {
            return !IsConnected
            ? throw new InvalidOperationException("The underlying client is not connected to a cache node")
            : _client!.AddOrUpdateObjectAsync(key, newKey, callback, state, cancellation);
        }

        ///<inheritdoc/>
        public override object GetUnderlyingStore() => _client ?? throw new InvalidOperationException("The client is not currently connected");

        private sealed class AuthManager : ICacheAuthManager
        {

            private IAsyncLazy<ReadOnlyJsonWebKey> _sigKey;
            private IAsyncLazy<ReadOnlyJsonWebKey> _verKey;

            public AuthManager(PluginBase plugin)
            {
                //Lazy load keys

                //Get the signing key
                _sigKey = plugin.GetSecretAsync("client_private_key").ToLazy(static r => r.GetJsonWebKey());

                //Lazy load cache public key
                _verKey = plugin.GetSecretAsync("cache_public_key").ToLazy(static r => r.GetJsonWebKey());
            }

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
            {
                Logger.Error("Failed to discover nodes from server {s} cause:\n{err}", errorNode?.NodeId, ex);
            }
        }
    }
}