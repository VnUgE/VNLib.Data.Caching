/*
* Copyright (c) 2024 Vaughn Nugent
* 
* Library: VNLib
* Package: ObjectCacheServer
* File: CacheListenerPubQueue.cs 
*
* CacheListenerPubQueue.cs is part of ObjectCacheServer which is part of the larger 
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
using System.Threading.Channels;
using System.Diagnostics.CodeAnalysis;

using VNLib.Utils.Async;
using VNLib.Utils.Logging;
using VNLib.Plugins;
using VNLib.Plugins.Extensions.Loading;

namespace VNLib.Data.Caching.ObjectCache.Server.Cache
{
    /*
     * Implements the event queue for the cache listener. Captures changes from the cache store
     * and publishes them to subscribers.
     * 
     * It also allows clients that are listening for changes to wait for events to 
     * their individual queues.
     */

    internal sealed class CacheListenerPubQueue : ICacheListenerEventQueue<IPeerEventQueue>, IAsyncBackgroundWork
    {
        private readonly AsyncQueue<ChangeEvent> _listenerQueue;
        private readonly ILogProvider _logProvider;
        private readonly PeerEventQueueManager _queueManager;

        public CacheListenerPubQueue(PluginBase plugin, PeerEventQueueManager queueMan)
        {
            _queueManager = queueMan;
            _logProvider = plugin.Log.CreateScope(CacheConstants.LogScopes.CacheListenerPubQueue);

            //Init local queue to store published events
            _listenerQueue = new(new BoundedChannelOptions(CacheConstants.CacheListenerChangeQueueSize)
            {
                AllowSynchronousContinuations = true,
                FullMode = BoundedChannelFullMode.DropOldest,
                SingleReader = true,    //Always a singe thread reading events 
                SingleWriter = false,
            });
        }

        ///<inheritdoc/>
        async Task IAsyncBackgroundWork.DoWorkAsync(ILogProvider _, CancellationToken exitToken)
        {
            const int accumulatorSize = 64;

            try
            {
                _logProvider.Debug("Change queue worker listening for local cache changes");

                //Accumulator for events
                ChangeEvent[] accumulator = new ChangeEvent[accumulatorSize];
                int index = 0;

                //Listen for changes
                while (true)
                {
                    //Wait for next event
                    accumulator[index++] = await _listenerQueue.DequeueAsync(exitToken);

                    //try to accumulate more events until we can't anymore
                    while (_listenerQueue.TryDequeue(out ChangeEvent? ev) && index < accumulatorSize)
                    {
                        accumulator[index++] = ev;
                    }

                    //Publish all events to subscribers
                    _queueManager.PublishMultiple(accumulator.AsSpan(0, index));

                    //Reset pointer
                    index = 0;
                }
            }
            catch (OperationCanceledException)
            {
                //Normal exit
                _logProvider.Debug("Change queue listener worker exited");
            }
        }

        ///<inheritdoc/>
        public bool IsEnabled([NotNullWhen(true)] IPeerEventQueue? userState) => userState is not null;

        ///<inheritdoc/>
        public void PublishEvent(ChangeEvent changeEvent)
        {
            if (!_listenerQueue.TryEnqueue(changeEvent))
            {
                _logProvider.Warn("Cache listener event queue is overflowing");
            }
        }

        ///<inheritdoc/>
        public bool TryDequeue(IPeerEventQueue userState, out ChangeEvent changeEvent)
        {
            return userState.TryDequeue(out changeEvent);
        }

        ///<inheritdoc/>
        public ValueTask<ChangeEvent> DequeueAsync(IPeerEventQueue userState, CancellationToken cancellation)
        {
            return userState.DequeueAsync(cancellation);
        }
    }
}
