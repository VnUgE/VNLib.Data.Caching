/*
* Copyright (c) 2023 Vaughn Nugent
* 
* Library: VNLib
* Package: VNLib.Plugins.Extensions.VNCache
* File: RemoteCacheOperator.cs 
*
* RemoteCacheOperator.cs is part of VNLib.Plugins.Extensions.VNCache which is 
* part of the larger VNLib collection of libraries and utilities.
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

using System;
using System.Threading;
using System.Threading.Tasks;

using VNLib.Data.Caching;
using VNLib.Plugins.Extensions.Loading;
using VNLib.Utils.Logging;

namespace VNLib.Plugins.Extensions.VNCache
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
    public sealed class RemoteCacheOperator : IAsyncBackgroundWork, IAsyncConfigurable
    {
        private readonly VnCacheClient _client;
        private CancellationTokenSource? _tokenSource;

        internal RemoteCacheOperator(VnCacheClient client)
        {
            _client = client;
        }

        /// <summary>
        /// The configured global cache instance
        /// </summary>
        public IGlobalCacheProvider Cache => _client;

        ///<inheritdoc/>
        ///<exception cref="ArgumentNullException"></exception>
        public async Task DoWorkAsync(ILogProvider pluginLog, CancellationToken exitToken)
        {
            _ = pluginLog ?? throw new ArgumentNullException(nameof(pluginLog));

            //Create cts linked to the exit token to allow user cancellation of the listener
            using(_tokenSource = CancellationTokenSource.CreateLinkedTokenSource(exitToken))
            {
                //Do work with linked source
                await _client.DoWorkAsync(pluginLog, _tokenSource.Token)
                    .ConfigureAwait(false);
            }

            //Remove cts
            _tokenSource = null;
        }

        /// <summary>
        /// Cancels the background cache client listener
        /// </summary>
        public void CancelListener() => _tokenSource?.Cancel();

        ///<inheritdoc/>
        public Task ConfigureServiceAsync(PluginBase plugin)
        {
            return _client.ConfigureServiceAsync(plugin);
        }
    }
}