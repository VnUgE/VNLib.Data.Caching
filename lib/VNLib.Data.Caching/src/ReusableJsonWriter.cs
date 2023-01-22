/*
* Copyright (c) 2022 Vaughn Nugent
* 
* Library: VNLib
* Package: VNLib.Data.Caching
* File: ReusableJsonWriter.cs 
*
* ReusableJsonWriter.cs is part of VNLib.Data.Caching which is part of the larger 
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

using System.IO;
using System.Buffers;
using System.Text.Json;

using VNLib.Utils;

namespace VNLib.Data.Caching
{
    internal sealed class ReusableJsonWriter : VnDisposeable
    {
        private readonly Utf8JsonWriter _writer;

        public ReusableJsonWriter()
        {
            _writer = new(Stream.Null);
        }

        /// <summary>
        /// Serializes the message and writes the serialzied data to the buffer writer
        /// </summary>
        /// <typeparam name="T"></typeparam>
        /// <param name="writer">The buffer writer to store data at</param>
        /// <param name="value">The object to serialize</param>
        /// <param name="options">Optional - serializer options</param>
        public void Serialize<T>(IBufferWriter<byte> writer, T value, JsonSerializerOptions? options = null)
        {
            //Init the writer with the new buffer writer
            _writer.Reset(writer);
            try
            {
                //Serialize message
                JsonSerializer.Serialize(_writer, value, options);
                //Flush writer to underlying buffer
                _writer.Flush();
            }
            finally
            {
                //Unlink the writer
                _writer.Reset(Stream.Null);
            }
        }

        protected override void Free() => _writer.Dispose();
    }
}
