/*
* Copyright (c) 2024 Vaughn Nugent
* 
* Library: VNLib
* Package: VNLib.Data.Caching.Providers.VNCache
* File: VNCacheBase.cs 
*
* VNCacheBase.cs is part of VNLib.Data.Caching.Providers.VNCache 
* which is part of the larger VNLib collection of libraries and utilities.
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

using System.Threading;
using System.Threading.Tasks;

using VNLib.Plugins;
using VNLib.Plugins.Extensions.Loading;

namespace VNLib.Data.Caching.Providers.VNCache
{
    internal abstract class VNCacheBase(VNCacheConfig conf) : IGlobalCacheProvider
    {
        ///<inheritdoc/>
        public abstract bool IsConnected { get; }

        ///<inheritdoc/>
        public virtual ICacheObjectDeserializer DefaultDeserializer => conf.CacheObjectDeserializer!;

        ///<inheritdoc/>
        public virtual ICacheObjectSerializer DefaultSerializer => conf.CacheObjectSerializer!;
     
        ///<inheritdoc/>
        public abstract Task AddOrUpdateAsync<T>(string key, string? newKey, T value, ICacheObjectSerializer serialzer, CancellationToken cancellation);

        ///<inheritdoc/>
        public abstract Task AddOrUpdateAsync<T>(string key, string? newKey, ObjectDataGet<T> callback, T state, CancellationToken cancellation);

        ///<inheritdoc/>
        public abstract Task<bool> DeleteAsync(string key, CancellationToken cancellation);

        ///<inheritdoc/>
        public abstract Task<T?> GetAsync<T>(string key, ICacheObjectDeserializer deserializer, CancellationToken cancellation);

        ///<inheritdoc/>
        public abstract Task GetAsync<T>(string key, ObjectDataSet<T> callback, T state, CancellationToken cancellation);

        ///<inheritdoc/>
        public abstract object GetUnderlyingStore();

        /// <summary>
        /// Initializes a set of cache object serializers and deserializers
        /// for the configuration instance and loads external serializers if specified
        /// by the user.
        /// </summary>
        /// <param name="config">The configuration instance to initialzie</param>
        /// <param name="plugin">Optional plugin for loading external serializers</param>
        protected static void InitSerializers(VNCacheConfig config, PluginBase? plugin)
        {
            //See if user has specified a custom serializer assembly
            if (!string.IsNullOrWhiteSpace(config.SerializerDllPath))
            {
                //Load the custom serializer assembly and get the serializer and deserializer instances
                config.CacheObjectSerializer = plugin.CreateServiceExternal<ICacheObjectSerializer>(config.SerializerDllPath);

                //Avoid creating another instance if the deserializer is the same as the serializer
                if(config.CacheObjectSerializer is ICacheObjectDeserializer cod)
                {
                    config.CacheObjectDeserializer = cod;
                }
                else
                {
                    config.CacheObjectDeserializer = plugin.CreateServiceExternal<ICacheObjectDeserializer>(config.SerializerDllPath);
                }
            }

            //If no default serializer is set, use the default JSON serializer
            config.CacheObjectSerializer ??= new JsonCacheObjectSerializer(256);
            config.CacheObjectDeserializer ??= new JsonCacheObjectSerializer(256);
        }
    }
}