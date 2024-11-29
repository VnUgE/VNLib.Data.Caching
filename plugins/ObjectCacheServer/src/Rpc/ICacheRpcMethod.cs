/*
* Copyright (c) 2024 Vaughn Nugent
* 
* Library: VNLib
* Package: ObjectCacheServer
* File: ICacheRpcMethod.cs 
*
* ICacheRpcMethod.cs is part of ObjectCacheServer which is part of the larger 
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

using System.Text.Json;
using System.Threading.Tasks;

using VNLib.Plugins.Essentials;

namespace VNLib.Data.Caching.ObjectCache.Server.Rpc
{
    public interface ICacheRpcMethod
    {
        /// <summary>
        /// The uniqe method name for this rpc method to be called by clients
        /// </summary>
        string Method { get; }

        /// <summary>
        /// Called when a client sends an rpc request to the server
        /// </summary>
        /// <param name="entity">The http entity requesting this method</param>
        /// <param name="request">The json request argument data send by the client</param>
        /// <returns>The result of the operation to be sent to the caller</returns>
        Task<CacheRpcResult> OnRequestAsync(HttpEntity entity, JsonElement request);
    }
}
