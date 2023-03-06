/*
* Copyright (c) 2023 Vaughn Nugent
* 
* Library: VNLib
* Package: VNLib.Data.Caching.Extensions
* File: ActiveServer.cs 
*
* ActiveServer.cs is part of VNLib.Data.Caching.Extensions which is part of the larger 
* VNLib collection of libraries and utilities.
*
* VNLib.Data.Caching.Extensions is free software: you can redistribute it and/or modify 
* it under the terms of the GNU Affero General Public License as 
* published by the Free Software Foundation, either version 3 of the
* License, or (at your option) any later version.
*
* VNLib.Data.Caching.Extensions is distributed in the hope that it will be useful,
* but WITHOUT ANY WARRANTY; without even the implied warranty of
* MERCHANTABILITY or FITNESS FOR A PARTICULAR PURPOSE. See the
* GNU Affero General Public License for more details.
*
* You should have received a copy of the GNU Affero General Public License
* along with this program.  If not, see https://www.gnu.org/licenses/.
*/

using System;
using System.Text.Json.Serialization;

namespace VNLib.Data.Caching.Extensions
{
    public class ActiveServer
    {
        [JsonPropertyName("address")]
        public string? HostName { get; set; }
        [JsonPropertyName("server_id")]
        public string? ServerId { get; set; }
        [JsonPropertyName("ip_address")]
        public string? Ip { get; set; }
        ///<inheritdoc/>
        public override int GetHashCode() => ServerId!.GetHashCode(StringComparison.OrdinalIgnoreCase);
        ///<inheritdoc/>
        public override bool Equals(object? obj) => obj is ActiveServer s && GetHashCode() == s.GetHashCode();
    }
}
