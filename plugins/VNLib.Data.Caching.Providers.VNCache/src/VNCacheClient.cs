/*
* Copyright (c) 2025 Vaughn Nugent
* 
* Library: VNLib
* Package: VNLib.Data.Caching.Providers.VNCache
* File: VNCacheClient.cs 
*
* VNCacheClient.cs is part of VNLib.Data.Caching.Providers.VNCache which is 
* part of the larger VNLib collection of libraries and utilities.
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
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using System.Text.Json.Serialization;

using VNLib.Utils.Logging;
using VNLib.Plugins;
using VNLib.Plugins.Extensions.Loading;
using VNLib.Plugins.Extensions.Loading.Configuration;
using VNLib.Data.Caching.Providers.VNCache.Internal;

/*
 * This package exports an IGlobalCacheProvider that is intended to be packaged by 
 * application distributors that want to use VNCache as a global cache for their 
 * application.
 * 
 * This package allows for memory only caching, write-through memory cache, and 
 * direct remote caching using VNCache as the backend.
 */

namespace VNLib.Data.Caching.Providers.VNCache
{

    /// <summary>
    /// The VNCache global cache provider client, that is intended to be loaded
    /// using <see cref="LoadingExtensions.GetOrCreateSingleton{T}(PluginBase)"/> directly
    /// on the plugin loading a cache client.
    /// <para>
    /// Users may also create cache instances outside of plugin context using static 
    /// methods.
    /// </para>
    /// </summary>
    [ServiceExport]
    [ConfigurationName("cache")]
    public sealed class VNCacheClient : ICacheClient, IAsyncBackgroundWork
    {       
        internal const string MEMORY_CACHE_CONFIG_KEY = "memory_cache";
        internal const string MEMORY_CACHE_ONLY_KEY = "memory_only";

        /*
         * Sets the default buffer size for the cache object serializer
         * when using the default serializer in plugin context.
         */
        private const int DefaultSerializerBufSize = 256;

        /// <summary>
        /// Allows you to programatically create a remote-only VNCache instance
        /// </summary>
        /// <param name="config">The remote cache configuration, required for VNCache remote cache servers</param>
        /// <returns>An opreator handle that can schedule the remote cache worker task</returns>
        /// <exception cref="ArgumentNullException"></exception>
        /// <remarks>
        /// The returned <see cref="VNCacheClientHandle"/> implements the <see cref="IAsyncBackgroundWork"/>
        /// interface and must be scheduled in order to maintain a connection with the remote cache store.
        /// </remarks>
        public static VNCacheClientHandle CreateRemoteCache(VNCacheClientConfig config)
        {
            ArgumentNullException.ThrowIfNull(config);

            config.OnValidate();
          
            //Create a new client that depends on the plugin context
            FBMCacheClient client = new(config);

            return new(client);
        }

        /// <summary>
        /// Allows you to programatically create your own instance if a VNCache remote server backed
        /// memory cache programatically. 
        /// </summary>
        /// <param name="remote">The remote cache configuration, required for VNCache remote cache servers</param>
        /// <param name="memory">The local memory backed configuration</param>
        /// <returns>An opreator handle that can schedule the remote cache worker task</returns>
        /// <exception cref="ArgumentNullException"></exception>
        /// <remarks>
        /// The returned <see cref="VNCacheClientHandle"/> implements the <see cref="IAsyncBackgroundWork"/>
        /// interface and must be scheduled in order to maintain a connection with the remote cache store. The memory cache 
        /// resources are released when the worker task exits.
        /// </remarks>
        public static VNCacheClientHandle CreateRemoteBackedMemoryCache(VNCacheClientConfig remote, VNMemoryCacheConfig memory)
        {
            ArgumentNullException.ThrowIfNull(remote);
            ArgumentNullException.ThrowIfNull(memory);
          
            remote.OnValidate();
            memory.OnValidate();

            //Remove cache requires an auth manager
            Validate.NotNull(remote.AuthManager, "You must explicitly configure an authentication manager");

            //Create a new client that depends on the plugin context
            FBMCacheClient fbmCache = new(remote);

            RemoteBackedMemoryCache cache = new(memory, fbmCache);
            return new(cache);
        }

        /// <summary>
        /// Allows you to programatically create a memory only <see cref="ICacheClient"/>
        /// cache instance.
        /// </summary>
        /// <param name="config">The memory cache configuration</param>
        /// <returns>
        /// A <see cref="MemoryCacheOperator"/> handle that holds a ready-to use cache instance. 
        /// This operator must be disposed to release held resources.
        /// </returns>
        /// <exception cref="ArgumentNullException"></exception>
        public static VNCacheClientHandle CreateMemoryCache(VNMemoryCacheConfig config)
        {
            ArgumentNullException.ThrowIfNull(config);

            config.OnValidate();

            return new VNCacheClientHandle(cache: new MemoryCache(config));
        }

        private readonly PluginBase _plugin;
        private readonly ICacheClient _client;
        private readonly VNCacheClientHandle _handle;

        public VNCacheClient(PluginBase plugin, IConfigScope config)
        {
            ArgumentNullException.ThrowIfNull(plugin);
            ArgumentNullException.ThrowIfNull(config);

            //Read the cache client configuration object
            VNCacheClientConfig cacheClientConfig = config.Deserialize<VNCacheClientConfig>()!;
            PluginConfigJson extendedConfig = config.Deserialize<PluginConfigJson>()!;

            //Always assign a debug log in plugin context
            cacheClientConfig.ClientDebugLog = plugin.Log.CreateScope("CLIENT");

            //Create a jwk authenticator from plugin secrets
            cacheClientConfig.AuthManager = JwkAuthManager.FromLazyJwk(
                sigKey: plugin.GetSecretAsync("client_private_key").ToJsonWebKey().AsLazy(),
                verifKey: plugin.GetSecretAsync("cache_public_key").ToJsonWebKey().AsLazy()
            );

            // Set the debug flag if the plugin is in debug mode
            cacheClientConfig.IsDebug |= plugin.IsDebug();

            InitSerializers(plugin, extendedConfig, cacheClientConfig);

            if (config.TryGetValue(MEMORY_CACHE_CONFIG_KEY, out JsonElement memCacheConfEl))
            {
                VNMemoryCacheConfig memoryConfig = memCacheConfEl.Deserialize<VNMemoryCacheConfig>()!;

                //Use the plugin configuration to create the memory manager
                memoryConfig.MemoryManagerFactory = plugin.GetOrCreateSingleton<BucketLocalManagerFactory>();

                //Assign the serializers from the cache client config
                memoryConfig.CacheObjectSerializer = cacheClientConfig.CacheObjectSerializer;
                memoryConfig.CacheObjectDeserializer = cacheClientConfig.CacheObjectDeserializer;

                if (extendedConfig.MemoryOnly)
                {                   
                    _handle = CreateMemoryCache(memoryConfig);
                }
                else
                {
                    _handle = CreateRemoteBackedMemoryCache(
                        remote: cacheClientConfig,
                        memory: memoryConfig                  
                    );
                }

                plugin.Log.Verbose("Enabling memory cache. MemoryOnly={memOnly}", extendedConfig.MemoryOnly);
            }
            else
            {
                _handle = CreateRemoteCache(cacheClientConfig);
            }

            _plugin = plugin;
            _client = _handle.Cache;
        }

        private static void InitSerializers(PluginBase plugin, PluginConfigJson pluginConfig, VNCacheClientConfig config)
        {
            /*
             * When running in plugin context, the user may have specified a custom 
             * serializer assembly to load. If so, we need to load the assembly and
             * get the serializer instances.
             */

            if (!string.IsNullOrWhiteSpace(pluginConfig.SerializerDllPath))
            {
                //Load the custom serializer assembly and get the serializer and deserializer instances
                config.CacheObjectSerializer = plugin.CreateServiceExternal<ICacheObjectSerializer>(pluginConfig.SerializerDllPath);

                //Avoid creating another instance if the deserializer is the same as the serializer
                if (config.CacheObjectSerializer is ICacheObjectDeserializer cod)
                {
                    config.CacheObjectDeserializer = cod;
                }
                else
                {
                    config.CacheObjectDeserializer = plugin.CreateServiceExternal<ICacheObjectDeserializer>(pluginConfig.SerializerDllPath);
                }
            }
            else
            {
                //Use the default serializer and deserializer
                config.CacheObjectSerializer = new JsonCacheObjectSerializer(DefaultSerializerBufSize);
                config.CacheObjectDeserializer = new JsonCacheObjectSerializer(DefaultSerializerBufSize);
            }
        }


        ///<inheritdoc/>
        Task IAsyncBackgroundWork.DoWorkAsync(ILogProvider pluginLog, CancellationToken exitToken)
        {
            /*
             * When running in plugin context, this function will be called. It will
             * invoke the run function for the backing cache store (using the operator 
             * handle)
             * 
             * The handle will internally link to the cancellation token so we don't need
             * to explicitly call the StopListening function.
             */
            return _handle.RunInternalAsync(
                _plugin,
                operationLog: pluginLog.CreateScope("VNCache"), 
                exitToken
            );
        }

        ///<inheritdoc/>
        public bool IsConnected => _client.IsConnected;

        ///<inheritdoc/>
        public ICacheObjectDeserializer DefaultDeserializer => _client.DefaultDeserializer;

        ///<inheritdoc/>
        public ICacheObjectSerializer DefaultSerializer => _client.DefaultSerializer;

        ///<inheritdoc/>
        public Task AddOrUpdateAsync<T>(string key, string? newKey, T value, ICacheObjectSerializer serialzer, CancellationToken cancellation)
        {
            return _client.AddOrUpdateAsync(key, newKey, value, serialzer, cancellation);
        }

        ///<inheritdoc/>
        public Task AddOrUpdateAsync<T>(string key, string? newKey, ObjectDataGet<T> callback, T state, CancellationToken cancellation)
        {
            return _client.AddOrUpdateAsync(key, newKey, callback, state, cancellation);
        }

        ///<inheritdoc/>
        public Task<bool> DeleteAsync(string key, CancellationToken cancellation)
        {
            return _client.DeleteAsync(key, cancellation);
        }

        ///<inheritdoc/>
        public Task<T?> GetAsync<T>(string key, ICacheObjectDeserializer deserializer, CancellationToken cancellation)
        {
            return _client.GetAsync<T>(key, deserializer, cancellation);
        }

        ///<inheritdoc/>
        public Task GetAsync<T>(string key, ObjectDataSet<T> callback, T state, CancellationToken cancellation)
        {
            return _client.GetAsync(key, callback, state, cancellation);
        }

        ///<inheritdoc/>
        public object GetUnderlyingStore()
        {
            return _client.GetUnderlyingStore();
        }
        
        private sealed class PluginConfigJson
        {
            /// <summary>
            /// Optional external cache serializer library to load
            /// </summary>
            [JsonPropertyName("serializer_assembly")]
            public string? SerializerDllPath { get; set; }

            /// <summary>
            /// A value that indicates if memory only caching is enabled
            /// </summary>
            [JsonPropertyName("memory_only")]
            public bool MemoryOnly { get; set; }

        }
    }
}
