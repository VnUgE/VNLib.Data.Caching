﻿/*
* Copyright (c) 2023 Vaughn Nugent
* 
* Library: VNLib
* Package: VNLib.Plugins.Extensions.VNCache
* File: ICacheKeyGenerator.cs 
*
* ICacheKeyGenerator.cs is part of VNLib.Plugins.Extensions.VNCache 
* which is part of the larger VNLib collection of libraries and utilities.
*
* VNLib.Plugins.Extensions.VNCache is free software: you can redistribute it and/or modify 
* it under the terms of the GNU Affero General Public License as 
* published by the Free Software Foundation, either version 3 of the
* License, or (at your option) any later version.
*
* VNLib.Plugins.Extensions.VNCache is distributed in the hope that it will be useful,
* but WITHOUT ANY WARRANTY; without even the implied warranty of
* MERCHANTABILITY or FITNESS FOR A PARTICULAR PURPOSE. See the
* GNU Affero General Public License for more details.
*
* You should have received a copy of the GNU Affero General Public License
* along with this program.  If not, see https://www.gnu.org/licenses/.
*/

namespace VNLib.Plugins.Extensions.VNCache.DataModel
{
    /// <summary>
    /// An interface that provides 1:1 entity to cache key mapping
    /// </summary>
    public interface ICacheKeyGenerator
    {
        /// <summary>
        /// Computes the unique key identifying the item within
        /// the cache store, unique to the store.
        /// </summary>
        /// <param name="entityId">The id of the entity to get the key for</param>
        /// <returns>The unique key identifying the item</returns>
        string ComputedKey(string entityId);
    }
}
