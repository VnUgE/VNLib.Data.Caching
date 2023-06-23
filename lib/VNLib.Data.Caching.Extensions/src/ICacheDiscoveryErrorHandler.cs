/*
* Copyright (c) 2023 Vaughn Nugent
* 
* Library: VNLib
* Package: VNLib.Data.Caching.Extensions
* File: ClientCacheConfiguration.cs 
*
* ClientCacheConfiguration.cs is part of VNLib.Data.Caching.Extensions which is part of the larger 
* VNLib collection of libraries and utilities.
*
* VNLib.Data.Caching.Extensions is free software: you can redistribute it and/or modify 
* it under the terms of the GNU Affero General Public License as 
* published by the Free Software Foundation, either version 3 of the
* License, or (at your option) any later version.
*
* VNLib.Data.Caching.Extensions is distributed in the hope that it will be useful,
* but WITHOUT ANY WARRANTY; without even the implied warranty of
* MERCHANTABILITY or FITNESS FOR A PARTICULAR PURPOSE. See the
* GNU Affero General Public License for more details.
*
* You should have received a copy of the GNU Affero General Public License
* along with this program.  If not, see https://www.gnu.org/licenses/.
*/

using System;

namespace VNLib.Data.Caching.Extensions
{
    /// <summary>
    /// Represents an type that will handle errors that occur during the discovery process
    /// </summary>
    public interface ICacheDiscoveryErrorHandler
    {
        /// <summary>
        /// Invoked when an error occurs during the discovery process
        /// </summary>
        /// <param name="errorNode">The node that the error occured on</param>
        /// <param name="ex">The exception that caused the invocation</param>
        void OnDiscoveryError(ICacheNodeAdvertisment errorNode, Exception ex);
    }
}
