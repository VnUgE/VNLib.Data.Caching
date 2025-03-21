/*
* Copyright (c) 2025 Vaughn Nugent
* 
* Library: VNLib
* Package: VNLib.Data.Caching.Providers.VNCache
* File: CacheClientHandle.cs 
*
* CacheClientHandle.cs is part of VNLib.Data.Caching.Providers.VNCache which is 
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

using System;
using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;

using VNLib.Utils;
using VNLib.Utils.Logging;
using VNLib.Plugins;
using VNLib.Plugins.Extensions.Loading;
using VNLib.Data.Caching.Providers.VNCache.Internal;

namespace VNLib.Data.Caching.Providers.VNCache
{

    /// <summary>
    /// Represents a handle to a VNCache cache client, that exposes a cancellable 
    /// <see cref="IAsyncBackgroundWork"/> to run inside a <see cref="PluginBase"/>
    /// or standlone in your own background work handler
    /// </summary>
    /// <remarks>
    /// The background work method must be sheduled for the cache client to be 
    /// connected to the backing store
    /// </remarks>
    public sealed class VNCacheClientHandle(ICacheClient cache) : VnDisposeable, IAsyncBackgroundWork
    {
        private CancellationTokenSource? _tokenSource;

        /// <summary>
        /// The configured global cache instance
        /// </summary>
        public ICacheClient Cache { get; } = cache;

        ///<inheritdoc/>
        Task IAsyncBackgroundWork.DoWorkAsync(ILogProvider pluginLog, CancellationToken exitToken)
        {
            return RunAsync(pluginLog, exitToken);
        }

        /// <summary>
        /// Begins the lifecycle of a cache cluster client by discovering cluster nodes
        /// choosing a node, and running a connection loop with the cluster. 
        /// <para>
        /// Unless running in a plugin context, you must call this function to begin the
        /// cache client. DO NOT call this function if running in plugin context, it will
        /// be scheduled in the background.
        /// </para>
        /// <para>
        /// This function will not exit unless an unrecoverable error occurs, 
        /// or the exit token is cancelled. You should always provide a cancellation
        /// token to this function to allow for graceful shutdown.
        /// </para>
        /// </summary>
        /// <param name="operationLog">A log provider to write connection and logging data to</param>
        /// <param name="exitToken">A token that will gracefully stop a client connection when cancelled</param>
        /// <returns>A task that represents this background operation</returns>
        public Task RunAsync(ILogProvider operationLog, CancellationToken exitToken = default)
        {
            return RunInternalAsync(null, operationLog, exitToken);
        }

        /*
         * Allows for running in plugin context internally 
         */
        internal async Task RunInternalAsync(PluginBase? plugin, ILogProvider operationLog, CancellationToken exitToken = default)
        {
            ArgumentNullException.ThrowIfNull(operationLog);

            //Create cts linked to the exit token to allow user cancellation of the listener
            using (_tokenSource = CancellationTokenSource.CreateLinkedTokenSource(exitToken))
            {
                if (Cache is IInternalCacheClient vncache)
                {
                    /*
                     * Internal cache stores may implement their own connection logic
                     * that needs to be run in the background. This ensures a common
                     * mode of operation for all cache clients.
                     */

                    await vncache.RunAsync(plugin, operationLog, _tokenSource.Token)
                        .ConfigureAwait(false);
                }
                else if (Cache is IAsyncBackgroundWork client)
                {
                    Debug.Fail("Background work api is deprecated for internal providers");

                    await client.DoWorkAsync(operationLog, _tokenSource.Token)
                        .ConfigureAwait(false);
                }
            }

            //Remove cts
            _tokenSource = null;
        }

        /// <summary>
        /// Cancels the background cache client listener
        /// </summary>
        public void StopListening() => _tokenSource?.Cancel();

        protected override void Free()
        {
            if (Cache is IDisposable disposable)
            {
                disposable.Dispose();
            }

            _tokenSource?.Dispose();
        }
    }
}