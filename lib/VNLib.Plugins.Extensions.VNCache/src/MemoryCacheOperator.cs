/*
* Copyright (c) 2023 Vaughn Nugent
* 
* Library: VNLib
* Package: VNLib.Plugins.Extensions.VNCache
* File: MemoryCacheOperator.cs 
*
* MemoryCacheOperator.cs is part of VNLib.Plugins.Extensions.VNCache which is
* part of the larger VNLib collection of libraries and utilities.
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

using VNLib.Data.Caching;
using VNLib.Utils;

namespace VNLib.Plugins.Extensions.VNCache
{
    /// <summary>
    /// A disposable memory cache operator handle. When cache use is complete, you should 
    /// dispose this handle. You may want to schedule it for cleanup on a <see cref="PluginBase"/>
    /// </summary>
    public sealed class MemoryCacheOperator : VnDisposeable
    {
        private readonly MemoryCache _cache;

        internal MemoryCacheOperator(MemoryCache cache)
        {
            _cache = cache;
        }

        /// <summary>
        /// The configured global cache instance
        /// </summary>
        public IGlobalCacheProvider Cache => _cache;

        ///<inheritdoc/>
        protected override void Free()
        {
            _cache.Dispose();
        }
    }
}