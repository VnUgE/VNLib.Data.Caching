/*
* Copyright (c) 2025 Vaughn Nugent
* 
* Library: VNLib
* Package: VNLib.Data.Caching
* File: JsonCacheObjectSerializer.cs 
*
* JsonCacheObjectSerializer.cs is part of VNLib.Data.Caching which is part of the larger 
* VNLib collection of libraries and utilities.
*
* VNLib.Data.Caching is free software: you can redistribute it and/or modify 
* it under the terms of the GNU Affero General Public License as 
* published by the Free Software Foundation, either version 3 of the
* License, or (at your option) any later version.
*
* VNLib.Data.Caching is distributed in the hope that it will be useful,
* but WITHOUT ANY WARRANTY; without even the implied warranty of
* MERCHANTABILITY or FITNESS FOR A PARTICULAR PURPOSE. See the
* GNU Affero General Public License for more details.
*
* You should have received a copy of the GNU Affero General Public License
* along with this program.  If not, see https://www.gnu.org/licenses/.
*/

using System;

using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace VNLib.Data.Caching
{
    /// <summary>
    /// Implements a <see cref="ICacheObjectDeserializer"/> and a <see cref="ICacheObjectSerializer"/>
    /// that uses JSON serialization, with writer pooling. Members of this class are thread-safe.
    /// </summary>
    public class JsonCacheObjectSerializer : ICacheObjectSerializer, ICacheObjectDeserializer
    {

        private readonly JsonSerializerOptions? _options;

        /// <summary>
        /// Initializes a new <see cref="JsonCacheObjectSerializer"/>
        /// </summary>
        /// <param name="options">JSON serialization/deserialization options</param>
        public JsonCacheObjectSerializer(JsonSerializerOptions options) 
            => _options = options;

        /// <summary>
        /// Initializes a new <see cref="JsonCacheObjectSerializer"/> using 
        /// the default serialization rules
        /// </summary>
        public JsonCacheObjectSerializer(int bufferSize)
        {
            //Configure default serialzation options
            _options = new()
            {
                DictionaryKeyPolicy = JsonNamingPolicy.CamelCase,
                NumberHandling = JsonNumberHandling.Strict,
                ReadCommentHandling = JsonCommentHandling.Disallow,
                WriteIndented = false,
                DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
                IgnoreReadOnlyFields = true,
                PropertyNameCaseInsensitive = true,
                IncludeFields = false,
                DefaultBufferSize = bufferSize
            };
        }

        ///<inheritdoc/>
        public virtual T? Deserialize<T>(ReadOnlySpan<byte> objectData) 
            => JsonSerializer.Deserialize<T>(objectData, _options);

        ///<inheritdoc/>
        public virtual void Serialize<T>(T obj, Stream finiteWriter)
        {          
            JsonSerializer.Serialize(finiteWriter, obj, _options);
        }
    }
}
