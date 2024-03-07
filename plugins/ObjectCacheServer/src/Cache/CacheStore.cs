/*
* Copyright (c) 2024 Vaughn Nugent
* 
* Library: VNLib
* Package: ObjectCacheServer
* File: CacheStore.cs 
*
* CacheStore.cs is part of ObjectCacheServer which is part of the larger 
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

using System;
using System.Threading;
using System.Threading.Tasks;

using VNLib.Plugins.Extensions.Loading;

namespace VNLib.Data.Caching.ObjectCache.Server.Cache
{

    /*
     * Implements the blob cache store, which is an abstraction around the blob cache listener.
     * This allows for publishing local events (say from other nodes) to keep caches in sync.
     */

    [ConfigurationName("cache")]
    internal sealed class CacheStore(IBlobCacheTable table) : ICacheStore
    {

        ///<inheritdoc/>
        ValueTask ICacheStore.AddOrUpdateBlobAsync<T>(string objectId, string? alternateId, ObjectDataGet<T> bodyData, T state, CancellationToken token)
        {
            return table.AddOrUpdateObjectAsync(objectId, alternateId, bodyData, state, default, token);
        }

        ///<inheritdoc/>
        void ICacheStore.Clear()
        {
            throw new NotImplementedException();
        }

        ///<inheritdoc/>
        ValueTask<bool> ICacheStore.DeleteItemAsync(string id, CancellationToken token)
        {
            return table.DeleteObjectAsync(id, token);
        }
    }
}
