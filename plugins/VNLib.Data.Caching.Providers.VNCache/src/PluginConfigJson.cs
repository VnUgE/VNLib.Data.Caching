/*
* Copyright (c) 2025 Vaughn Nugent
* 
* Library: VNLib
* Package: VNLib.Data.Caching.Providers.VNCache
* File: PluginConfigJson.cs 
*
* PluginConfigJson.cs is part of VNLib.Data.Caching.Providers.VNCache which is 
* part of the larger VNLib collection of libraries and utilities.
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

using System.Text.Json.Serialization;

namespace VNLib.Data.Caching.Providers.VNCache
{
    internal sealed class PluginConfigJson: VNCacheConfig
    {
        /// <summary>
        /// Optional external cache serializer library to load
        /// </summary>
        [JsonPropertyName("serializer_assembly")]
        public string? SerializerDllPath { get; init; }

        /// <summary>
        /// A value that indicates if memory only caching is enabled
        /// </summary>
        [JsonPropertyName("memory_only")]
        public bool MemoryOnly { get; init; }

        /// <summary>
        /// A value that indicates if the cache client is in debug mode
        /// </summary>
        [JsonPropertyName("memory_cache")]
        public VNMemoryCacheConfig? MemoryCacheConfig { get; init; }

        /// <summary>
        /// Specifies the size of the buffer to use inside the default serialzier instance.
        /// </summary>
        [JsonPropertyName("json_serializer_buffer_size")]
        public int JsonSerializerBufferSize { get; init; } = 256;
    }
}
