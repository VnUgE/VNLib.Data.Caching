/*
* Copyright (c) 2023 Vaughn Nugent
* 
* Library: VNLib
* Package: ObjectCacheServer
* File: CacheEventQueueManager.cs 
*
* CacheEventQueueManager.cs is part of ObjectCacheServer which is 
* part of the larger VNLib collection of libraries and utilities.
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
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Threading.Channels;
using System.Collections.Generic;

using VNLib.Plugins;
using VNLib.Utils.Async;
using VNLib.Utils.Logging;
using VNLib.Plugins.Extensions.Loading;
using VNLib.Plugins.Extensions.Loading.Events;


namespace VNLib.Data.Caching.ObjectCache.Server.Cache
{
    internal sealed class CacheEventQueueManager : ICacheEventQueueManager, IDisposable, IIntervalScheduleable
    {
        private readonly int MaxQueueDepth;

        private readonly object SubLock;
        private readonly LinkedList<NodeQueue> Subscribers;

        private readonly object StoreLock;
        private readonly Dictionary<string, NodeQueue> QueueStore;


        public CacheEventQueueManager(PluginBase plugin)
        {
            //Get node config
            NodeConfig config = plugin.GetOrCreateSingleton<NodeConfig>();

            //Get max queue depth
            MaxQueueDepth = config.MaxQueueDepth;

            /*
             * Schedule purge interval to clean up stale queues
             */
            plugin.ScheduleInterval(this, config.EventQueuePurgeInterval);

            SubLock = new();
            Subscribers = new();

            StoreLock = new();
            QueueStore = new(StringComparer.OrdinalIgnoreCase);
        }

        ///<inheritdoc/>
        public IPeerEventQueue Subscribe(ICachePeer peer)
        {
            NodeQueue? nq;

            bool isNew = false;

            //Enter sync lock
            lock (StoreLock)
            {
                //Try to recover the queue for the node
                if (!QueueStore.TryGetValue(peer.NodeId, out nq))
                {
                    //Create new queue
                    nq = new(peer.NodeId, MaxQueueDepth);
                    QueueStore.Add(peer.NodeId, nq);
                    isNew = true;
                }

                //Increment listener count
                nq.Listeners++;
            }

            //Publish new peer to subscribers list
            if (isNew)
            {
                lock (SubLock)
                {
                    //Add peer to subscribers list
                    Subscribers.AddLast(nq);
                }
            }

            //Return the node's queue
            return nq;
        }

        ///<inheritdoc/>
        public void Unsubscribe(ICachePeer peer)
        {
            //Detach a listener for a node
            lock (StoreLock)
            {
                //Get the queue and decrement the listener count
                NodeQueue nq = QueueStore[peer.NodeId];
                nq.Listeners--;
            }
        }

        ///<inheritdoc/>
        public void PublishSingle(ChangeEvent change)
        {
            //Wait to enter the sub lock
            lock (SubLock)
            {
                //Loop through ll the fast way
                LinkedListNode<NodeQueue>? q = Subscribers.First;

                while (q != null)
                {
                    //Pub single event node
                    q.Value.PublishChange(change);

                    //Get next queue
                    q = q.Next;
                }
            }
        }

        ///<inheritdoc/>
        public void PublishMultiple(Span<ChangeEvent> changes)
        {
            //Wait to enter the sub lock
            lock (SubLock)
            {
                //Loop through ll the fast way
                LinkedListNode<NodeQueue>? q = Subscribers.First;

                while (q != null)
                {
                    //Publish multiple
                    q.Value.PublishChanges(changes);

                    //Get next queue
                    q = q.Next;
                }
            }
        }

        ///<inheritdoc/>
        public void PurgeStaleSubscribers()
        {
            //Enter locks
            lock (SubLock)
            {
                lock (StoreLock)
                {
                    //Get all stale queues (queues without listeners)
                    NodeQueue[] staleQueues = QueueStore.Values.Where(static nq => nq.Listeners == 0).ToArray();

                    foreach (NodeQueue nq in staleQueues)
                    {
                        //Remove from store
                        QueueStore.Remove(nq.NodeId);

                        //remove from subscribers
                        Subscribers.Remove(nq);
                    }
                }
            }
        }

        //Interval to purge stale subscribers
        Task IIntervalScheduleable.OnIntervalAsync(ILogProvider log, CancellationToken cancellationToken)
        {
            log.Debug("Purging stale peer event queues");

            PurgeStaleSubscribers();

            return Task.CompletedTask;
        }

        void IDisposable.Dispose()
        {
            QueueStore.Clear();
            Subscribers.Clear();
        }

        /*
         * Holds queues for each node and keeps track of the number of listeners
         * attached to the queue
         */

        private sealed class NodeQueue : IPeerEventQueue
        {
            public int Listeners;

            public string NodeId { get; }

            public AsyncQueue<ChangeEvent> Queue { get; }

            public NodeQueue(string nodeId, int maxDepth)
            {
                NodeId = nodeId;

                /*
                 * Create a bounded channel that acts as a lru and evicts 
                 * the oldest item when the queue is full
                 * 
                 * There will also only ever be a single thread writing events 
                 * to the queue
                 */

                BoundedChannelOptions queueOptions = new(maxDepth)
                {
                    AllowSynchronousContinuations = true,
                    SingleReader = false,
                    SingleWriter = true,
                    //Drop oldest item in queue if full
                    FullMode = BoundedChannelFullMode.DropOldest,
                };

                //Init queue/channel
                Queue = new(queueOptions);
            }

            public void PublishChange(ChangeEvent change)
            {
                Queue.TryEnque(change);
            }

            public void PublishChanges(Span<ChangeEvent> changes)
            {
                for (int i = 0; i < changes.Length; i++)
                {
                    Queue.TryEnque(changes[i]);
                }
            }

            ///<inheritdoc/>
            public ValueTask<ChangeEvent> DequeueAsync(CancellationToken cancellation)
            {
                return Queue.DequeueAsync(cancellation);
            }

            ///<inheritdoc/>
            public bool TryDequeue(out ChangeEvent change)
            {
                return Queue.TryDequeue(out change);
            }
        }
    }
}
