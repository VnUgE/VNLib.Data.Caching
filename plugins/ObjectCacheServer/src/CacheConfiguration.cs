/*
* Copyright (c) 2023 Vaughn Nugent
* 
* Library: VNLib
* Package: ObjectCacheServer
* File: CacheConfiguration.cs 
*
* CacheConfiguration.cs is part of ObjectCacheServer which is part of the larger 
* VNLib collection of libraries and utilities.
*
* ObjectCacheServer is free software: you can redistribute it and/or modify 
* it under the terms of the GNU Affero General Public License as 
* published by the Free Software Foundation, either version 3 of the
* License, or (at your option) any later version.
*
* ObjectCacheServer is distributed in the hope that it will be useful,
* but WITHOUT ANY WARRANTY; without even the implied warranty of
* MERCHANTABILITY or FITNESS FOR A PARTICULAR PURPOSE. See the
* GNU Affero General Public License for more details.
*
* You should have received a copy of the GNU Affero General Public License
* along with this program.  If not, see https://www.gnu.org/licenses/.
*/

using System.Text.Json.Serialization;

namespace VNLib.Data.Caching.ObjectCache.Server
{
    internal sealed class CacheConfiguration
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
      

        [JsonPropertyName("change_queue_max_depth")]
        public int MaxEventQueueDepth { get; set; } = 10 * 1000;


        [JsonPropertyName("max_cache")]
        public uint MaxCacheEntries { get; set; } = 10000;

        [JsonPropertyName("buckets")]
        public uint BucketCount { get; set; } = 10;
    }
}
