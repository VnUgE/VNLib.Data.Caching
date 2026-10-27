/*
* Copyright (c) 2026 Vaughn Nugent
* 
* Library: VNLib
* Package: VNLib.Data.Caching.VNCache.Server
* File: CacheMemoryConfiguration.cs 
*
* CacheMemoryConfiguration.cs is part of VNLib.Data.Caching.VNCache.Server which 
* is part of the larger VNLib collection of libraries and utilities.
*
* VNLib.Data.Caching.VNCache.Server is free software: you can redistribute it and/or modify 
* it under the terms of the GNU Affero General Public License as 
* published by the Free Software Foundation, either version 3 of the
* License, or (at your option) any later version.
*
* VNLib.Data.Caching.VNCache.Server is distributed in the hope that it will be useful,
* but WITHOUT ANY WARRANTY; without even the implied warranty of
* MERCHANTABILITY or FITNESS FOR A PARTICULAR PURPOSE. See the
* GNU Affero General Public License for more details.
*
* You should have received a copy of the GNU Affero General Public License
* along with this program.  If not, see https://www.gnu.org/licenses/.
*/

using System.Text.Json.Serialization;

namespace VNLib.Data.Caching.VNCache.Server.Cache
{
    internal sealed class CacheMemoryConfiguration
    {
        [JsonPropertyName("buffer_recv_max")]
        public int MaxRecvBufferSize { get; set; } = 1000 * 1024;
        [JsonPropertyName("buffer_recv_min")]
        public int MinRecvBufferSize { get; set; } = 8 * 1024;


        [JsonPropertyName("buffer_header_max")]
        public int MaxHeaderBufferSize { get; set; } = 2 * 1024;

        [JsonPropertyName("buffer_header_min")]
        public int MinHeaderBufferSize { get; set; } = 128;


        [JsonPropertyName("max_message_size")]
        public int MaxMessageSize { get; set; } = 1000 * 1024;


        [JsonPropertyName("max_cache")]
        public uint MaxCacheEntries { get; set; } = 10000;

        [JsonPropertyName("buckets")]
        public uint BucketCount { get; set; } = 10;


        [JsonPropertyName("memory_lib_path")]
        public string? ExternLibPath { get; set; }

        [JsonPropertyName("enable_checksums")]
        public bool EnableChecksums { get; set; } = true;
    }
}
