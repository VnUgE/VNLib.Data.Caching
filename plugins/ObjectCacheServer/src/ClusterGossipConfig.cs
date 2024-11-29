/*
* Copyright (c) 2024 Vaughn Nugent
* 
* Library: VNLib
* Package: ObjectCacheServer
* File: ServerClusterConfig.cs 
*
* ServerClusterConfig.cs is part of ObjectCacheServer which is part of the larger 
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

using VNLib.Plugins.Extensions.Loading;
using VNLib.Plugins.Extensions.Loading.Configuration;

namespace VNLib.Data.Caching.ObjectCache.Server
{
    internal sealed class ClusterGossipConfig : IOnConfigValidation
    {
        [JsonPropertyName("enabled")]
        public bool Enabled { get; init; }

        [JsonPropertyName("node_id")]
        public string NodeId { get; init; } = null!;

        [JsonPropertyName("port")]
        public ushort ServerPort { get; init; }

        public void OnValidate()
        {
            if (!Enabled)
            {
                return;
            }

            Validate.NotNull(NodeId, "node_id");
            Validate.Range(ServerPort, 1, 65535, "port");
        }
    }
}
