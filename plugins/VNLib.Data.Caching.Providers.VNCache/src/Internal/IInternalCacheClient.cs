/*
* Copyright (c) 2025 Vaughn Nugent
* 
* Library: VNLib
* Package: VNLib.Data.Caching.Providers.VNCache
* File: IInternalCacheClient.cs 
*
* IInternalCacheClient.cs is part of VNLib.Data.Caching.Providers.VNCache which is 
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

using System.Threading;
using System.Threading.Tasks;

using VNLib.Utils.Logging;

namespace VNLib.Data.Caching.Providers.VNCache.Internal
{
    internal interface IInternalCacheClient : ICacheClient
    {
        /// <summary>
        /// Run client-specific logic for enabling long-running client work in 
        /// the background for the duration of the plugin lifecycle
        /// </summary>
        /// <param name="operationLog">A log to write client specific information to</param>
        /// <param name="exitToken">A token called to cancel the background work</param>
        /// <returns>
        /// A task that completes when the background work has completed. It is assumed when this task
        /// complets, that the client work is done and may be disposed.
        /// </returns>
        internal Task RunAsync(ILogProvider operationLog, CancellationToken exitToken);
    }
}