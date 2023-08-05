/*
* Copyright (c) 2023 Vaughn Nugent
* 
* Library: VNLib
* Package: VNLib.Plugins.Extensions.VNCache
* File: ScopedCache.cs 
*
* ScopedCache.cs is part of VNLib.Plugins.Extensions.VNCache 
* which is part of the larger VNLib collection of libraries and utilities.
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

using System.Threading;
using System.Threading.Tasks;

using VNLib.Data.Caching;

namespace VNLib.Plugins.Extensions.VNCache.DataModel
{
    /// <summary>
    /// A cache that stores objects with 1:1 keys unique to this instance. That is, a unique entity 
    /// that is stored in this cache instance may only be retrieved, deleted, or updated, by the 
    /// same instance. This is an abstract class.
    /// </summary>
    public abstract class ScopedCache : IGlobalCacheProvider
    {

        /// <summary>
        /// The <see cref="ICacheKeyGenerator"/> to provide unique 
        /// cache keys
        /// </summary>
        protected abstract ICacheKeyGenerator KeyGen { get; }

        ///<inheritdoc/>
        public abstract bool IsConnected { get; }

        ///<inheritdoc/>
        public abstract Task AddOrUpdateAsync<T>(string key, string? newKey, T value, CancellationToken cancellation);

        ///<inheritdoc/>
        public abstract Task DeleteAsync(string key, CancellationToken cancellation);
       
        ///<inheritdoc/>
        public abstract Task<T?> GetAsync<T>(string key, CancellationToken cancellation);

        ///<inheritdoc/>
        public abstract Task<T?> GetAsync<T>(string key, ICacheObjectDeserialzer deserializer, CancellationToken cancellation);

        ///<inheritdoc/>
        public abstract Task AddOrUpdateAsync<T>(string key, string? newKey, T value, ICacheObjectSerialzer serialzer, CancellationToken cancellation);

        ///<inheritdoc/>
        public abstract Task GetAsync(string key, IObjectData rawData, CancellationToken cancellation);

        ///<inheritdoc/>
        public abstract Task AddOrUpdateAsync(string key, string? newKey, IObjectData rawData, ICacheObjectSerialzer serialzer, CancellationToken cancellation);
    }
}
