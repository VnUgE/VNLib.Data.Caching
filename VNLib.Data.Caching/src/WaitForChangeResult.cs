/*
* Copyright (c) 2022 Vaughn Nugent
* 
* Library: VNLib
* Package: VNLib.Data.Caching
* File: WaitForChangeResult.cs 
*
* WaitForChangeResult.cs is part of VNLib.Data.Caching which is part of the larger 
* VNLib collection of libraries and utilities.
*
* VNLib.Data.Caching is free software: you can redistribute it and/or modify 
* it under the terms of the GNU General Public License as published
* by the Free Software Foundation, either version 2 of the License,
* or (at your option) any later version.
*
* VNLib.Data.Caching is distributed in the hope that it will be useful,
* but WITHOUT ANY WARRANTY; without even the implied warranty of
* MERCHANTABILITY or FITNESS FOR A PARTICULAR PURPOSE. See the GNU 
* General Public License for more details.
*
* You should have received a copy of the GNU General Public License 
* along with VNLib.Data.Caching. If not, see http://www.gnu.org/licenses/.
*/

namespace VNLib.Data.Caching
{
    /// <summary>
    /// The result of a cache server change event
    /// </summary>
    public readonly struct WaitForChangeResult
    {
        /// <summary>
        /// The operation status code
        /// </summary>
        public readonly string Status { get; init; }
        /// <summary>
        /// The current (or old) id of the element that changed
        /// </summary>
        public readonly string CurrentId { get; init; }
        /// <summary>
        /// The new id of the element that changed
        /// </summary>
        public readonly string NewId { get; init; }
    }
}
