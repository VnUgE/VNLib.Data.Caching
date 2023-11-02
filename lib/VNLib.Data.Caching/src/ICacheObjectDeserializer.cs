/*
* Copyright (c) 2023 Vaughn Nugent
* 
* Library: VNLib
* Package: VNLib.Data.Caching
* File: ICacheObjectDeserializer.cs 
*
* ICacheObjectDeserialzer.cs is part of VNLib.Data.Caching which is part 
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

namespace VNLib.Data.Caching
{
    /// <summary>
    /// Provides custom binary deserialzation for a given type
    /// </summary>
    public interface ICacheObjectDeserializer
    {
        /// <summary>
        /// Attempts to deserialze the supplied binary buffer to its original
        /// object state.
        /// </summary>
        /// <param name="objectData">The buffer containing data to deserialze</param>
        /// <typeparam name="T"></typeparam>
        /// <returns>A new instance deserialzed to contain the original entity state</returns>
        T? Deserialize<T>(ReadOnlySpan<byte> objectData);
    }
}
