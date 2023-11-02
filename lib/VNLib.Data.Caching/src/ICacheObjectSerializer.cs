/*
* Copyright (c) 2023 Vaughn Nugent
* 
* Library: VNLib
* Package: VNLib.Data.Caching
* File: ICacheObjectSerializer.cs 
*
* ICacheObjectSerializer.cs is part of VNLib.Data.Caching which is part 
* of the larger VNLib collection of libraries and utilities.
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
using System.Buffers;

namespace VNLib.Data.Caching
{
    /// <summary>
    /// Provides custom binary deserialziation for a given type
    /// </summary>
    public interface ICacheObjectSerializer
    {
        /// <summary>
        /// Serializes an instance of the given type and writes
        /// the output to the supplied buffer writer
        /// </summary>
        /// <typeparam name="T">The type to serialze</typeparam>
        /// <param name="obj">The object instance to serialize</param>
        /// <param name="finiteWriter">A finite sized buffer writer to commit the serialized data to</param>
        void Serialize<T>(T obj, IBufferWriter<byte> finiteWriter);
    }
}
