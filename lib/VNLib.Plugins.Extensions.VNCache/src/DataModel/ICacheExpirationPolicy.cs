/*
* Copyright (c) 2024 Vaughn Nugent
* 
* Library: VNLib
* Package: VNLib.Plugins.Extensions.VNCache
* File: ICacheExpirationPolicy.cs 
*
* ICacheExpirationPolicy.cs is part of VNLib.Plugins.Extensions.VNCache 
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
    /// Provides an expiration policy for cache entities
    /// </summary>
    /// <typeparam name="TEntity"></typeparam>
    public interface ICacheExpirationPolicy<TEntity>
    {
        /// <summary>
        /// Determines if the entity is expired and should be 
        /// reloaded from the backing store.
        /// </summary>
        /// <param name="result">The entity to check status of</param>
        /// <returns>True of the entity has expired and should be reloaded, false if it is still a valid result</returns>
        bool IsExpired(TEntity result);

        /// <summary>
        /// Fired directly after the entity has been refreshed
        /// </summary>
        /// <param name="entity">The entity freshly fetched from the backing store</param>
        virtual void OnRefreshed(TEntity entity)
        { }
    }
   
}
