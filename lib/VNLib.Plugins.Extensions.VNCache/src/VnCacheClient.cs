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

            //Init the client with default settings
            FBMClientConfig conf = FBMDataCacheExtensions.GetDefaultConfig(MemoryUtil.Shared, config.MaxMessageSize!.Value, config.RequestTimeout, debugLog);

            Client = new(conf);

            //Add the configuration to the client
            Client.GetCacheConfiguration()
                .WithTls(config.UseTls)
                .WithInitialPeers(config.InitialNodes!);
        }

        public Task ConfigureServiceAsync(PluginBase plugin)
        {
            //Set authenticator
            Client.GetCacheConfiguration()
                .WithAuthenticator(new AuthManager(plugin))
                .WithErrorHandler(new DiscoveryErrHAndler(plugin.Log));

            return Task.CompletedTask;
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
                    ICacheNodeAdvertisment[]? servers;
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
                        ICacheNodeAdvertisment selected = await Client.ConnectToRandomCacheAsync(exitToken);
                        pluginLog.Debug("Connected to cache server {s}", selected.NodeId);

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
                using RSA? rsa = _sigKey.Value.GetRSAPublicKey();
                if(rsa != null)
                {
                    return rsa.SignHash(hash, alg.GetAlgName(), RSASignaturePadding.Pkcs1);
                }

                //try to get the ecdsa alg for the signing key
                using ECDsa? ecdsa = _sigKey.Value.GetECDsaPublicKey();
                if(ecdsa != null)
                {
                    return ecdsa.SignHash(hash);
                }

                throw new NotSupportedException("The signing key is not a valid RSA or ECDSA key");
            }

            ///<inheritdoc/>
            public bool VerifyJwt(JsonWebToken jwt)
            {
                return jwt.VerifyFromJwk(_verKey.Value);
            }

            ///<inheritdoc/>
            public bool VerifyMessageHash(ReadOnlySpan<byte> hash, HashAlg alg, ReadOnlySpan<byte> signature)
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
            public void OnDiscoveryError(ICacheNodeAdvertisment errorNode, Exception ex)
            {
                Logger.Error("Failed to discover nodes from server {s} cause:\n{err}", errorNode.NodeId, ex);
            }
        }
    }
}