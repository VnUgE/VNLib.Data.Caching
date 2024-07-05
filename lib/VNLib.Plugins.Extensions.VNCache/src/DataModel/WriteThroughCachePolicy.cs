/*
* Copyright (c) 2024 Vaughn Nugent
* 
* Library: VNLib
* Package: VNLib.Plugins.Extensions.VNCache
* File: WriteThroughCachePolicy.cs 
*
* WriteThroughCachePolicy.cs is part of VNLib.Plugins.Extensions.VNCache 
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

using System.Threading.Tasks;

namespace VNLib.Plugins.Extensions.VNCache.DataModel
{
    /// <summary>
    /// Provides a very simple Write-Through cache policy that 
    /// will cause fetch functions to block until the cache is 
    /// consistent with the backing store
    /// </summary>
    public class WriteThroughCachePolicy : ICacheTaskPolicy
    {
        /// <summary>
        /// The singleton instance of the write-through cache policy
        /// </summary>
        public static readonly WriteThroughCachePolicy Instance = new();

        ///<inheritdoc/>
        public Task ObserveOperationAsync(Task operation) => operation; //Write through
    }
   
}
