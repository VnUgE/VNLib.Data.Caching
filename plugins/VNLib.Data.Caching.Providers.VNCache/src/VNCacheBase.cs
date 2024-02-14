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

namespace VNLib.Data.Caching.Providers.VNCache
{
    internal abstract class VNCacheBase : IGlobalCacheProvider
    {
        ///<inheritdoc/>
        public abstract bool IsConnected { get; }

        ///<inheritdoc/>
        public virtual ICacheObjectDeserializer DefaultDeserializer { get; }

        ///<inheritdoc/>
        public virtual ICacheObjectSerializer DefaultSerializer { get; }

        protected VNCacheBase(VNCacheConfig config)
        {
            //Set default serializers
            DefaultDeserializer = config.CacheObjectDeserializer ?? new JsonCacheObjectSerializer(256);
            DefaultSerializer = config.CacheObjectSerializer ?? new JsonCacheObjectSerializer(256);
        }

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
    }
}