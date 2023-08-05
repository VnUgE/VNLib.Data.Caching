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

using VNLib.Hashing;
using VNLib.Hashing.IdentityUtility;
using VNLib.Utils.Memory;
using VNLib.Utils.Logging;
using VNLib.Data.Caching;
using VNLib.Data.Caching.Extensions;
using VNLib.Data.Caching.ObjectCache;
using VNLib.Net.Messaging.FBM.Client;
using VNLib.Plugins.Extensions.Loading;
using VNLib.Data.Caching.Extensions.Clustering;
using VNLib.Plugins.Extensions.Loading.Events;
using VNLib.Plugins.Extensions.VNCache.Clustering;

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
    internal class VnCacheClient : IGlobalCacheProvider, IAsyncBackgroundWork
    {
        private const string LOG_NAME = "CLIENT";
        private static readonly TimeSpan InitialDelay = TimeSpan.FromSeconds(10);
        private static readonly TimeSpan NoNodeDelay = TimeSpan.FromSeconds(10);

        private readonly VnCacheClientConfig _config;
        private readonly ClusterNodeIndex _index;

        /// <summary>
        /// The internal client
        /// </summary>
        public FBMClient Client { get; }

        /// <summary>
        /// Gets a value that determines if the client is currently connected to a server
        /// </summary>
        public bool IsConnected { get; private set; }

        public VnCacheClient(PluginBase plugin, IConfigScope config)
            :this(
                 config.Deserialze<VnCacheClientConfig>(),
                 plugin.IsDebug() ? plugin.Log : null
                )
        {
            ILogProvider scoped = plugin.Log.CreateScope(LOG_NAME);

            //Set authenticator and error handler
            Client.GetCacheConfiguration()
                .WithAuthenticator(new AuthManager(plugin))
                .WithErrorHandler(new DiscoveryErrHAndler(scoped));

            //Schedule discovery interval
            plugin.ScheduleInterval(_index, _config.DiscoveryInterval);

            //Run discovery after initial delay if interval is greater than initial delay
            if(_config.DiscoveryInterval > InitialDelay)
            {
                //Run a manual initial load
                scoped.Information("Running initial discovery in {delay}", InitialDelay);
                _ = plugin.ObserveWork(() => _index.OnIntervalAsync(scoped, plugin.UnloadToken), (int)InitialDelay.TotalMilliseconds);
            }
        }

        public VnCacheClient(VnCacheClientConfig config, ILogProvider? debugLog)
        {
            //Validate config
            (config as IOnConfigValidation).Validate();

            _config = config;

            //Init the client with default settings
            FBMClientConfig conf = FBMDataCacheExtensions.GetDefaultConfig(MemoryUtil.Shared, config.MaxMessageSize!.Value, config.RequestTimeout, debugLog);

            Client = new(conf);

            //Add the configuration to the client
            Client.GetCacheConfiguration()
                .WithTls(config.UseTls)
                .WithInitialPeers(config.GetInitialNodeUris());

            //Init index
            _index = new ClusterNodeIndex(Client.GetCacheConfiguration());
        }

        /*
         * Background work method manages the remote cache connection
         * to the cache cluster
         */
        public virtual async Task DoWorkAsync(ILogProvider pluginLog, CancellationToken exitToken)
        {
            //Scope log
            pluginLog = pluginLog.CreateScope(LOG_NAME);

            try
            {      
                //Initial delay
                pluginLog.Debug("Worker started, waiting for startup delay");
                await Task.Delay((int)InitialDelay.TotalMilliseconds + 1000, exitToken);

                while (true)
                {
                    try
                    {
                        //Wait for a discovery to complete  
                        await _index.WaitForDiscoveryAsync(exitToken);
                    }
                    catch(CacheDiscoveryFailureException cdfe)
                    {
                        pluginLog.Error("Failed to discover nodes, will try again\n{err}", cdfe.Message);
                        //Continue
                    }

                    //Get the next node to connect to
                    CacheNodeAdvertisment? node = _index.GetNextNode();

                    if (node is null)
                    {
                        pluginLog.Warn("No nodes available to connect to, trying again in {delay}", NoNodeDelay);
                        await Task.Delay(NoNodeDelay, exitToken);

                        //Run another manual discovery if the interval is greater than the delay
                        if (_config.DiscoveryInterval > NoNodeDelay)
                        {
                            pluginLog.Debug("Forcing a manual discovery");
                            
                            //We dont need to await this because it is awaited at the top of the loop
                            _ = _index.OnIntervalAsync(pluginLog, exitToken);
                        }

                        continue;
                    }

                    //Ready to connect

                    try
                    {
                        pluginLog.Debug("Connecting to {node}", node);

                        //Connect to the node
                        await Client.ConnectToCacheAsync(node, exitToken);

                        pluginLog.Debug("Successfully connected to {s}", node);

                        //Set connection status flag
                        IsConnected = true;

                        //Wait for disconnect
                        await Client.WaitForExitAsync(exitToken);

                        pluginLog.Debug("Cache server disconnected");
                    }
                    catch (WebSocketException wse)
                    {
                        pluginLog.Warn("Failed to establish a websocket connection to cache server {reason}", wse.Message);
                        continue;
                    }
                    //SEs may be raised when the server is not available
                    catch (HttpRequestException he) when (he.InnerException is SocketException)
                    {
                        pluginLog.Debug("Failed to connect to random cache server because a TCP connection could not be established");
                        //Continue next loop
                        continue;
                    }
                    finally
                    {
                        IsConnected = false;
                    }

                    //Loop again
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

        ///<inheritdoc/>
        public virtual Task GetAsync(string key, IObjectData rawData, CancellationToken cancellation)
        {
            return !IsConnected
              ? throw new InvalidOperationException("The underlying client is not connected to a cache node")
              : Client!.GetObjectAsync(key, rawData, cancellation);
        }

        ///<inheritdoc/>
        public virtual Task AddOrUpdateAsync(string key, string? newKey, IObjectData rawData, ICacheObjectSerialzer serialzer, CancellationToken cancellation)
        {
            return !IsConnected
            ? throw new InvalidOperationException("The underlying client is not connected to a cache node")
            : Client!.AddOrUpdateObjectAsync(key, newKey, rawData, serialzer, cancellation);
        }

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
                if(rsa != null)
                {
                    return rsa.SignHash(hash, alg.GetAlgName(), RSASignaturePadding.Pkcs1);
                }

                //try to get the ecdsa alg for the signing key
                using ECDsa? ecdsa = _sigKey.Value.GetECDsaPrivateKey();
                if(ecdsa != null)
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