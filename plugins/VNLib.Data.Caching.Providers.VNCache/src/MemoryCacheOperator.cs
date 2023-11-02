/*
* Copyright (c) 2023 Vaughn Nugent
* 
* Library: VNLib
* Package: VNLib.Data.Caching.Providers.VNCache
* File: MemoryCacheOperator.cs 
*
* MemoryCacheOperator.cs is part of VNLib.Data.Caching.Providers.VNCache which is
* part of the larger VNLib collection of libraries and utilities.
*
* VNLib.Data.Caching.Providers.VNCache is free software: you can redistribute it and/or modify 
* it under the terms of the GNU Affero General Public License as 
* published by the Free Software Foundation, either version 3 of the
* License, or (at your option) any later version.
*
* VNLib.Data.Caching.Providers.VNCache is distributed in the hope that it will be useful,
* but WITHOUT ANY WARRANTY; without even the implied warranty of
* MERCHANTABILITY or FITNESS FOR A PARTICULAR PURPOSE. See the
* GNU Affero General Public License for more details.
*
* You should have received a copy of the GNU Affero General Public License
* along with this program.  If not, see https://www.gnu.org/licenses/.
*/

using VNLib.Utils;
using VNLib.Plugins;

namespace VNLib.Data.Caching.Providers.VNCache
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