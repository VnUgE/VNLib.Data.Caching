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

namespace VNLib.Plugins.Extensions.VNCache
{
    internal sealed class MemoryCacheConfig : ICacheRefreshPolicy
    {
        [JsonPropertyName("buckets")]
        public uint TableSize { get; set; } = 10;

        [JsonPropertyName("bucket_size")]
        public uint BucketSize { get; set; } = 5000;

        [JsonPropertyName("max_object_size")]
        public uint MaxBlobSize { get; set; } = 16 * 1024; 

        [JsonIgnore]
        public TimeSpan MaxCacheAge { get; set; } = TimeSpan.FromMinutes(1);

        [JsonPropertyName("max_age_sec")]
        public uint MaxAgeSec
        {
            get => (uint)MaxCacheAge.TotalSeconds;
            set => MaxCacheAge = TimeSpan.FromSeconds(value);
        }
        /*
         * Default disable cache
         */
        public TimeSpan RefreshInterval { get; set; } = TimeSpan.Zero;

        [JsonPropertyName("refresh_interval_sec")]
        public uint RefreshSec
        {
            get => (uint)RefreshInterval.TotalSeconds;
            set => RefreshInterval = TimeSpan.FromSeconds(value);
        }

        [JsonPropertyName("write_through")]
        public bool WriteThrough { get; set; } = true;
    }
}