/*
* Copyright (c) 2025 Vaughn Nugent
* 
* Library: VNLib
* Package: VNLib.Data.Caching.Providers.VNCache
* File: VNMemoryCacheConfig.cs 
*
* VNMemoryCacheConfig.cs is part of VNLib.Data.Caching.Providers.VNCache 
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

using VNLib.Data.Caching.ObjectCache;
using VNLib.Plugins.Extensions.Loading.Configuration;

namespace VNLib.Data.Caching.Providers.VNCache
{
    /// <summary>
    /// Memorycache configuration object
    /// </summary>
    public sealed class VNMemoryCacheConfig : VNCacheConfig
    {
        /// <summary>
        /// Optionally sets the memory manager the cache provider will use
        /// to manage memory
        /// </summary>
        [JsonIgnore]
        public ICacheMemoryManagerFactory? MemoryManagerFactory { get; set; }

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

        ///<inheritdoc/>
        public override void OnValidate()
        {
            base.OnValidate();

            Validate.Assert(TableSize > 0, "You must specify a number of cache buckets");
            Validate.Assert(BucketSize > 0, "You must specify a 'bucket_size'");
        }
    }
}