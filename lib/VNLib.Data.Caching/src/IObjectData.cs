/*
* Copyright (c) 2023 Vaughn Nugent
* 
* Library: VNLib
* Package: VNLib.Data.Caching
* File: IObjectData.cs 
*
* IObjectData.cs is part of VNLib.Data.Caching which is part of the larger 
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

namespace VNLib.Data.Caching
{
    /// <summary>
    /// Represents object data to send or receive from a cache server
    /// </summary>
    public interface IObjectData
    {
        /// <summary>
        /// Gets the cachable objects data to store in the remote cache server
        /// </summary>
        /// <returns>The object data to set</returns>
        ReadOnlySpan<byte> GetData();

        /// <summary>
        /// Stores the retrieved object data from the remote server
        /// </summary>
        /// <param name="data">The object data found on the remote server</param>
        void SetData(ReadOnlySpan<byte> data);
    }
}
