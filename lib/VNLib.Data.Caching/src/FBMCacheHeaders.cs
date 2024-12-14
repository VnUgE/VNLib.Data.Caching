/*
* Copyright (c) 2024 Vaughn Nugent
* 
* Library: VNLib
* Package: VNLib.Data.Caching
* File: FBMCacheHeaders.cs 
*
* FBMCacheHeaders.cs is part of VNLib.Data.Caching which is part of the larger 
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

using System.Linq;

using VNLib.Net.Messaging.FBM;
using VNLib.Net.Messaging.FBM.Server;

using static VNLib.Data.Caching.Constants;

namespace VNLib.Data.Caching
{
    /// <summary>
    /// Provides caching hemper methods for <see cref="FBMContext"/>
    /// </summary>
    public static class FBMCacheHeaders
    {
        /// <summary>
        /// Gets the request method string
        /// </summary>
        /// <returns>The request method string</returns>
        public static string? GetMethod(FBMContext context)
        {
            return context.Request.Headers
                .FirstOrDefault(static kvp => kvp.Header == HeaderCommand.Action)
                .GetValueString();
        }

        /// <summary>
        /// Gets the Object-id for the request message
        /// </summary>
        /// <param name="context">The server message context</param>
        /// <returns>The id of the object requested</returns>
        public static string? GetObjectId(FBMContext context)
        {
            return context.Request.Headers
                .FirstOrDefault(static kvp => kvp.Header == ObjectId)
                .GetValueString();
        }


        /// <summary>
        /// Gets the new ID of the object if specified from the request. Null if the request did not specify an id update
        /// </summary>
        /// <param name="context">The server message context</param>
        /// <returns>The new ID of the object if speicifed, null otherwise</returns>
        public static string? GetNewObjectId(FBMContext context)
        {
            return context.Request.Headers
                .FirstOrDefault(static kvp => kvp.Header == NewObjectId)
                .GetValueString();
        }
    }
}
