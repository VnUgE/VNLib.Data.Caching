/*
* Copyright (c) 2025 Vaughn Nugent
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

using VNLib.Utils.Logging;

namespace VNLib.Data.Caching.Providers.VNCache.Internal
{
    internal abstract class VNCacheBase(VNCacheConfig conf) : IInternalCacheClient
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

        ///<inheritdoc/>
        public abstract Task RunAsync(ILogProvider operationLog, CancellationToken exitToken);
        
    }
}