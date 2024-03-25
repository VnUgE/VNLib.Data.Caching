/*
* Copyright (c) 2024 Vaughn Nugent
* 
* Library: VNLib
* Package: VNLib.Data.Caching.Providers.Redis
* File: RedisClientCacheEntry.cs 
*
* RedisClientCacheEntry.cs is part of VNLib.Data.Caching.Providers.Redis 
* which is part of the larger VNLib collection of libraries and utilities.
*
* VNLib.Data.Caching.Providers.Redis is free software: you can redistribute it and/or modify 
* it under the terms of the GNU Affero General Public License as 
* published by the Free Software Foundation, either version 3 of the
* License, or (at your option) any later version.
*
* VNLib.Data.Caching.Providers.Redis is distributed in the hope that it will be useful,
* but WITHOUT ANY WARRANTY; without even the implied warranty of
* MERCHANTABILITY or FITNESS FOR A PARTICULAR PURPOSE. See the
* GNU Affero General Public License for more details.
*
* You should have received a copy of the GNU Affero General Public License
* along with this program.  If not, see https://www.gnu.org/licenses/.
*/


using System;
using System.Buffers;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

using StackExchange.Redis;

using VNLib.Utils;
using VNLib.Utils.Memory;
using VNLib.Utils.Logging;
using VNLib.Utils.Extensions;
using VNLib.Plugins;
using VNLib.Plugins.Extensions.Loading;

namespace VNLib.Data.Caching.Providers.Redis
{
    /*
     * This package exports an IGlobalCacheProvider that is intended to be packaged by 
     * application distributors that want to use Redis as a global cache for their 
     * application.
     * 
     * The IGlobalCacheProvider primarily performs get/set operations on raw memory 
     * where possible. Custom serializers are allowed to be used for object serialziation.
     * 
     * The interface also requires that implementations provide a fallback serialization 
     * method. For now, this is a JSON serializer. But will likely have more complex 
     * decision making where possible, such as protobufs
     */

    [ServiceExport]
    [ConfigurationName("cache")]
    public sealed class RedisClientCacheEntry : IGlobalCacheProvider
    {
        private const int InitialWriterBufferSize = 4096;
    
        private readonly IUnmangedHeap _defaultHeap;
        private readonly Task OnLoadTask;
      

        private ConnectionMultiplexer? _redis;
        private IDatabase? _database;

        public RedisClientCacheEntry(PluginBase plugin, IConfigScope config)
        {
            _defaultHeap = MemoryUtil.Shared;

            ILogProvider redisLog = plugin.Log.CreateScope("REDIS");

            //Allow a raw connection string to be used
            if(config.ContainsKey("connection_string"))
            {
                string connectionString = config.GetRequiredProperty("connection_string", el => el.GetString()!);

                //Store load task so it can be awaited by the host
                OnLoadTask = Task.Run(async () =>
                {

                    if(connectionString.Contains("password=[SECRET]", StringComparison.OrdinalIgnoreCase))
                    {
                        //Load the password from the secret store and replace the placeholder with the found secret
                        using ISecretResult password = await plugin.GetSecretAsync("redis_password");
                        connectionString = connectionString.Replace("password=[SECRET]", $"password={password.Result}", StringComparison.OrdinalIgnoreCase);
                    }

                    redisLog.Information("Connecting to Redis server...");

                    //Connect to the server
                    _redis = await ConnectionMultiplexer.ConnectAsync(connectionString);

                    _database = _redis.GetDatabase();

                    redisLog.Information("Successfully connected to Redis server");
                });
            }
            else
            {
                ConfigurationOptions options = GetOptionsFromConfig(config);

                //Store load task so it can be awaited by the host
                OnLoadTask = Task.Run(async () =>
                {
                    //Retrieve the password last
                    using ISecretResult password = await plugin.GetSecretAsync("redis_password");
                    options.Password = password.Result.ToString();

                    redisLog.Information("Connecting to Redis server...");

                    //Connect to the server
                    _redis = await ConnectionMultiplexer.ConnectAsync(options);

                    _database = _redis.GetDatabase();

                    redisLog.Information("Successfully connected to Redis server");
                });
            }

            string? serialzerDllPath = config.GetPropString("serializer_assebly_name");

            //See if user has specified a custom serializer assembly
            if (!string.IsNullOrWhiteSpace(serialzerDllPath))
            {
                //Load the custom serializer assembly and get the serializer and deserializer instances
                DefaultSerializer = plugin.CreateServiceExternal<ICacheObjectSerializer>(serialzerDllPath);

                //Avoid creating another instance if the deserializer is the same as the serializer
                if (DefaultSerializer is ICacheObjectDeserializer cod)
                {
                    DefaultDeserializer = cod;
                }
                else
                {
                    DefaultDeserializer = plugin.CreateServiceExternal<ICacheObjectDeserializer>(serialzerDllPath);
                }
            }
            else
            {
                //If no default serializer is set, use the default JSON serializer
                DefaultDeserializer = new JsonCacheObjectSerializer(256);
                DefaultSerializer = new JsonCacheObjectSerializer(256);
            }
        }

        private static ConfigurationOptions GetOptionsFromConfig(IConfigScope config)
        {
            //Try go get the hostname
            string? hostname = config.GetRequiredProperty("url", p => p.GetString()!);
            Uri serverUri = new(hostname, UriKind.RelativeOrAbsolute);

            ConfigurationOptions options = new()
            {
                Ssl = serverUri.Scheme == "rediss://",
            };

            //Add the host and port
            options.EndPoints.Add(serverUri.DnsSafeHost, serverUri.Port);

            //Get optional values
            if (config.TryGetValue("user", out JsonElement user))
            {
                options.User = user.GetString();
            }

            if (config.TryGetValue("keepalive_sec", out JsonElement keepaliveSec))
            {
                options.KeepAlive = keepaliveSec.GetInt32();
            }

            if (config.TryGetValue("timeout_ms", out JsonElement timeoutMs))
            {
                options.SyncTimeout = timeoutMs.GetInt32();
            }

            if (config.TryGetValue("connect_timeout_ms", out JsonElement connectTimeoutMs))
            {
                options.ConnectTimeout = connectTimeoutMs.GetInt32();
            }

            if (config.TryGetValue("abort_on_connect_fail", out JsonElement abortOnConnectFail))
            {
                options.AbortOnConnectFail = abortOnConnectFail.GetBoolean();
            }

            if (config.TryGetValue("allow_admin", out JsonElement allowAdmin))
            {
                options.AllowAdmin = allowAdmin.GetBoolean();
            }

            if (config.TryGetValue("connect_retry", out JsonElement connectRetry))
            {
                options.ConnectRetry = connectRetry.GetInt32();
            }

            if (config.TryGetValue("connect_timeout", out JsonElement connectTimeout))
            {
                options.ConnectTimeout = connectTimeout.GetInt32();
            }

            if (config.TryGetValue("default_database", out JsonElement defaultDatabase))
            {
                options.DefaultDatabase = defaultDatabase.GetInt32();
            }

            if (config.TryGetValue("keep_alive", out JsonElement keepAlive))
            {
                options.KeepAlive = keepAlive.GetInt32();
            }

            if (config.TryGetValue("name", out JsonElement name))
            {
                options.ClientName = name.GetString();
            }

            return options;
        }

        ///<inheritdoc/>
        public bool IsConnected => _redis?.IsConnected == true;

        //Called by the host to wait for the cache to be loaded
        public Task InitAsync() => OnLoadTask;

        ///<inheritdoc/>
        public ICacheObjectDeserializer DefaultDeserializer { get; }

        ///<inheritdoc/>
        public ICacheObjectSerializer DefaultSerializer { get; }

        ///<inheritdoc/>
        public async Task AddOrUpdateAsync<T>(string key, string? newKey, T value, ICacheObjectSerializer serialzer, CancellationToken cancellation)
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(key);
            ArgumentNullException.ThrowIfNull(serialzer);

            //Alloc update buffer
            using AddOrUpdateBuffer buffer = new(_defaultHeap, InitialWriterBufferSize, false);

            //Serialize the object
            serialzer.Serialize(value, buffer);

            //Update object data
            await _database.StringSetAsync(key, buffer.GetWrittenData());

            if (!string.IsNullOrWhiteSpace(newKey))
            {
               //also update the key
                await _database.KeyRenameAsync(key, newKey);
            }
        }

        ///<inheritdoc/>
        public async Task AddOrUpdateAsync<T>(string key, string? newKey, ObjectDataGet<T> callback, T state, CancellationToken cancellation)
        {
            /*
             * Because the redis database only allows ReadonlyMemory when 
             * updating keys, we must copy the object data into a temporary
             * heap buffer and then copy it into the database.
             */

            int length = 0;

            //Create a copy buffer and copy the object data into it
            using IMemoryOwner<byte> buffer = AllocAndCopy(callback, state, _defaultHeap, ref length);

            //Set the value at the old key
            await _database.StringSetAsync(key, buffer.Memory[..length]);

            //If required also update the key
            if (!string.IsNullOrWhiteSpace(newKey))
            {
                await _database.KeyRenameAsync(key, newKey);
            }
            
            static IMemoryOwner<byte> AllocAndCopy(ObjectDataGet<T> callback, T state, IUnmangedHeap heap, ref int length)
            {
                //Get the buffer from the callback
                ReadOnlySpan<byte> data = callback(state);
                length = data.Length;

                //Alloc the buffer on the desired heap and copy data into it
                IMemoryHandle<byte> buffer = heap.AllocAndCopy(data);

                return buffer.ToMemoryManager(true);
            }
        }

        ///<inheritdoc/>
        public async Task<bool> DeleteAsync(string key, CancellationToken cancellation)
        {
            RedisValue value = await _database.StringGetDeleteAsync(key);
            return value.IsNull == false;   //Should only be null if the key did not exist
        }

        ///<inheritdoc/>
        public async Task<T?> GetAsync<T>(string key, ICacheObjectDeserializer deserializer, CancellationToken cancellation)
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(key);
            ArgumentNullException.ThrowIfNull(deserializer);

            //Try to get the value from the cache
            RedisValue value = await _database.StringGetAsync(key);

            //If the value is found, set the raw data
            if (value.IsNull)
            {
                return default;
            }

            return deserializer.Deserialize<T>(((ReadOnlyMemory<byte>)value).Span);
        }

        ///<inheritdoc/>
        public async Task GetAsync<T>(string key, ObjectDataSet<T> callback, T state, CancellationToken cancellation)
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(key);
            ArgumentNullException.ThrowIfNull(callback);

            //Try to get the value from the cache
            RedisValue value = await _database.StringGetAsync(key);

            //If the value is found, set the raw data
            if (!value.IsNull)
            {
                //Invoke callback with object data
                callback(state, ((ReadOnlyMemory<byte>)value).Span);
            }
        }

        ///<inheritdoc/>
        public object GetUnderlyingStore()
        {
            return _database is null ? throw new InvalidOperationException("The cache store is not available") : _database;
        }

        private sealed class AddOrUpdateBuffer: VnDisposeable, IBufferWriter<byte>
        {
            private readonly MemoryHandle<byte> _handle;
            private readonly MemoryManager<byte> _manager;

            private int _position;

            public AddOrUpdateBuffer(IUnmangedHeap heap, int initialSize, bool zero)
            {
                _handle = heap.Alloc<byte>(CalNewSize(initialSize), zero);
                //Create memory manager around the memhandle that does not own the handle
                _manager = _handle.ToMemoryManager(false);
            }

            public void Advance(int count)
            {
                ArgumentOutOfRangeException.ThrowIfNegative(count);
                _position += count;
            }

            ///<inheritdoc/>
            public Memory<byte> GetMemory(int sizeHint = 0)
            {
                nint newSize = CalNewSize(sizeHint);

                //Resize if needed
                _handle.ResizeIfSmaller(newSize);

                //Return the memory
                return _manager.Memory.Slice(_position, sizeHint);
            }
            
            nint CalNewSize(int size) => MemoryUtil.NearestPage(size + _position);

            ///<inheritdoc/>
            public Span<byte> GetSpan(int sizeHint = 0)
            {
                nint newSize = CalNewSize(sizeHint);

                //Resize if needed
                _handle.ResizeIfSmaller(newSize);

                //Return the memory
                return _handle.AsSpan(_position);
            }

            /// <summary>
            /// Gets the written data
            /// </summary>
            /// <returns>The memory segment pointing to the data that was written by the serializer</returns>
            public ReadOnlyMemory<byte> GetWrittenData() => _manager.Memory[.._position];

            protected override void Free()
            {
                //Free the handle, dont need to free memory manager
                _handle.Dispose();
            }
        }
    }
}
