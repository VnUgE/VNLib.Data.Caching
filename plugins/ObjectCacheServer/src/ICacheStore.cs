/*
* Copyright (c) 2024 Vaughn Nugent
* 
* Library: VNLib
* Package: ObjectCacheServer
* File: ICacheStore.cs 
*
* ICacheStore.cs is part of ObjectCacheServer which is part of the larger 
* VNLib collection of libraries and utilities.
*
* ObjectCacheServer is free software: you can redistribute it and/or modify 
* it under the terms of the GNU Affero General Public License as 
* published by the Free Software Foundation, either version 3 of the
* License, or (at your option) any later version.
*
* ObjectCacheServer is distributed in the hope that it will be useful,
* but WITHOUT ANY WARRANTY; without even the implied warranty of
* MERCHANTABILITY or FITNESS FOR A PARTICULAR PURPOSE. See the
* GNU Affero General Public License for more details.
*
* You should have received a copy of the GNU Affero General Public License
* along with this program.  If not, see https://www.gnu.org/licenses/.
*/

using System.Threading;
using System.Threading.Tasks;

namespace VNLib.Data.Caching.ObjectCache.Server
{
    internal interface ICacheStore
    {
        /// <summary>
        /// Asynchronously adds or updates an object in the store and optionally update's its id
        /// </summary>
        /// <param name="objectId">The current (or old) id of the object</param>
        /// <param name="alternateId">An optional id to update the blob to</param>
        /// <param name="bodyData">A callback that returns the data for the blob</param>
        /// <param name="state">The state parameter to pass to the data callback</param>
        /// <param name="token">A token to cancel the async operation</param>
        /// <returns>A value task that represents the async operation</returns>
        ValueTask AddOrUpdateBlobAsync<T>(string objectId, string? alternateId, ObjectDataGet<T> bodyData, T state, CancellationToken token = default);

        /// <summary>
        /// Clears all items from the store
        /// </summary>
        void Clear();

        /// <summary>
        /// Asynchronously deletes a previously stored item
        /// </summary>
        /// <param name="id">The id of the object to delete</param>
        /// <param name="token">A token to cancel the async lock await</param>
        /// <returns>A task that completes when the item has been deleted</returns>
        ValueTask<bool> DeleteItemAsync(string id, CancellationToken token = default);
    }
}
