/*
* Copyright (c) 2023 Vaughn Nugent
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

using VNLib.Utils.Async;
using VNLib.Utils.Logging;
using VNLib.Plugins;
using VNLib.Plugins.Extensions.Loading;

namespace VNLib.Data.Caching.ObjectCache.Server
{
    internal sealed class CacheListenerPubQueue : ICacheListenerEventQueue, IAsyncBackgroundWork
    {
        private readonly AsyncQueue<ChangeEvent> _listenerQueue;
        private readonly ILogProvider _logProvider;
        private readonly ICacheEventQueueManager _queueManager;

        public CacheListenerPubQueue(PluginBase plugin)
        {
            _queueManager = plugin.GetOrCreateSingleton<CacheEventQueueManager>();
            _logProvider = plugin.Log;
            _listenerQueue = new AsyncQueue<ChangeEvent>(new BoundedChannelOptions(10000)
            {
                AllowSynchronousContinuations = true,
                FullMode = BoundedChannelFullMode.DropOldest,
                SingleReader = true,
                SingleWriter = false,
            });
        }

        ///<inheritdoc/>
        public async Task DoWorkAsync(ILogProvider pluginLog, CancellationToken exitToken)
        {
            const int accumulatorSize = 64;

            try
            {
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
                pluginLog.Debug("Change queue listener worker exited");
            }
        }
       
        ///<inheritdoc/>
        public bool IsEnabled(object userState)
        {
            return userState is IPeerEventQueue;
        }

        ///<inheritdoc/>
        public void PublishEvent(ChangeEvent changeEvent)
        {
            if (!_listenerQueue.TryEnque(changeEvent))
            {
                _logProvider.Warn("Cache listener event queue is overflowing");
            }
        }

        ///<inheritdoc/>
        public bool TryDequeue(object userState, out ChangeEvent changeEvent)
        {
            return (userState as IPeerEventQueue)!.TryDequeue(out changeEvent);
        }

        ///<inheritdoc/>
        public ValueTask<ChangeEvent> DequeueAsync(object userState, CancellationToken cancellation)
        {
            return (userState as IPeerEventQueue)!.DequeueAsync(cancellation);
        }
    }
}
