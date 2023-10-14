/*
* Copyright (c) 2023 Vaughn Nugent
* 
* Library: VNLib
* Package: VNLib.Plugins.Extensions.VNCache
* File: MemoryCacheConfig.cs 
*
* MemoryCacheConfig.cs is part of VNLib.Plugins.Extensions.VNCache 
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

using System;
using System.Text.Json.Serialization;

using VNLib.Plugins.Extensions.Loading;

namespace VNLib.Plugins.Extensions.VNCache
{
    /// <summary>
    /// Memorycache configuration object. Json-(de)serializable 
    /// </summary>
    public sealed class MemoryCacheConfig : ICacheRefreshPolicy, IOnConfigValidation
    {
        /// <summary>
        /// The number of buckets within the cache table
        /// </summary>
        [JsonPropertyName("buckets")]
        public uint TableSize { get; set; } = 10;

        /// <summary>
        /// The number of cache entries within each bucket
        /// </summary>
        [JsonPropertyName("bucket_size")]
        public uint BucketSize { get; set; } = 5000;

        /// <summary>
        /// The maxium size (in bytes) of each cache entry within any bucket
        /// </summary>
        [JsonPropertyName("max_object_size")]
        public uint MaxBlobSize { get; set; } = 16 * 1024; 

        [JsonIgnore]
        public TimeSpan MaxCacheAge { get; set; } = TimeSpan.FromMinutes(1);

        /// <summary>
        /// When refresh intervals are configured, The maxium cache entry age in seconds. 
        /// </summary>
        [JsonPropertyName("max_age_sec")]
        public uint MaxAgeSeconds
        {
            get => (uint)MaxCacheAge.TotalSeconds;
            set => MaxCacheAge = TimeSpan.FromSeconds(value);
        }
        /*
         * Default disable cache
         */
        [JsonIgnore]
        public TimeSpan RefreshInterval { get; set; } = TimeSpan.Zero;

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

        /// <summary>
        /// Zeros all cache entry memory allocations before they are used
        /// </summary>
        [JsonPropertyName("zero_all")]
        public bool ZeroAllAllocations { get; set; }

        ///<inheritdoc/>
        public void Validate()
        {
            if(TableSize == 0)
            {
                throw new ArgumentException("You must specify a cache bucket table size", "buckets");
            }

            if(BucketSize == 0)
            {
                throw new ArgumentException("You must specify the maxium number of entires allowed in each bucket ", "bucket_size");
            }

            if(MaxBlobSize < 16)
            {
                throw new ArgumentException("You must configure a maximum object size", "max_object_size");
            }
        }
    }
}