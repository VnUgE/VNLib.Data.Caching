/*
* Copyright (c) 2023 Vaughn Nugent
* 
* Library: VNLib
* Package: VNLib.Data.Caching.Providers.VNCache
* File: VNCacheConfig.cs 
*
* VNCacheConfig.cs is part of VNLib.Data.Caching.Providers.VNCache 
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

using System;
using System.Text.Json.Serialization;

using VNLib.Plugins.Extensions.Loading;

namespace VNLib.Data.Caching.Providers.VNCache
{

    /// <summary>
    /// VNCache configuration for all vncache type providers
    /// </summary>
    public abstract class VNCacheConfig : ICacheRefreshPolicy, IOnConfigValidation
    {
        /*
         * Default disable refreshing
         */
        ///<inheritdoc/>
        [JsonIgnore]
        public virtual TimeSpan RefreshInterval { get; set; } = TimeSpan.Zero;

        /// <summary>
        /// The time (in seconds) a cache entry refresh interval will occur
        /// if scheduled on a plugin
        /// </summary>
        [JsonPropertyName("refresh_interval_sec")]
        public uint RefreshIntervalSeconds
        {
            get => (uint)RefreshInterval.TotalSeconds;
            set => RefreshInterval = TimeSpan.FromSeconds(value);
        }

        ///<inheritdoc/>
        [JsonIgnore]
        public virtual TimeSpan MaxCacheAge { get; set; } = TimeSpan.FromMinutes(1);

        /// <summary>
        /// When refresh intervals are configured, The maxium cache entry age in seconds. 
        /// </summary>
        [JsonPropertyName("max_age_sec")]
        public uint MaxAgeSeconds
        {
            get => (uint)MaxCacheAge.TotalSeconds;
            set => MaxCacheAge = TimeSpan.FromSeconds(value);
        }

        /// <summary>
        /// The cache object deserializer to use
        /// </summary>
        [JsonIgnore]
        public ICacheObjectDeserializer? CacheObjectDeserializer { get; set; }

        /// <summary>
        /// The cache object serializer to use
        /// </summary>
        [JsonIgnore]
        public ICacheObjectSerializer? CacheObjectSerializer { get; set; }

        /// <summary>
        /// Zeros all cache entry memory allocations before they are used
        /// </summary>
        [JsonPropertyName("zero_all")]
        public bool ZeroAllAllocations { get; set; }

        /// <summary>
        /// The maxium size (in bytes) of each cache entry within any bucket
        /// </summary>
        [JsonPropertyName("max_object_size")]
        public virtual uint MaxBlobSize { get; set; } = 16 * 1024;

        public virtual void Validate()
        {
            if (MaxBlobSize < 16)
            {
                throw new ArgumentException("You must configure a maximum object size", "max_object_size");
            }
        }
        
    }
}