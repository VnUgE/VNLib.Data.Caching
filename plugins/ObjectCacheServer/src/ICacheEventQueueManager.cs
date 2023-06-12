/*
* Copyright (c) 2023 Vaughn Nugent
* 
* Library: VNLib
* Package: ObjectCacheServer
* File: ICacheEventQueueManager.cs 
*
* ICacheEventQueueManager.cs is part of ObjectCacheServer which is part of the larger 
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

using VNLib.Utils.Async;


namespace VNLib.Data.Caching.ObjectCache.Server
{
    /// <summary>
    /// Represents a managment system for publishing and subscribing to cache change events
    /// </summary>
    internal interface ICacheEventQueueManager
    {
        /// <summary>
        /// Publishes a change event to all subscribers
        /// </summary>
        /// <param name="change">The change event to publish</param>
        void PublishSingle(ChangeEvent change);

        /// <summary>
        /// Publishes multiple change events to all subscribers
        /// </summary>
        /// <param name="changes">The span of changes to publish to all subscribers</param>
        void PublishMultiple(Span<ChangeEvent> changes);

        /// <summary>
        /// Attatches a subscriber that will receive all published changes
        /// </summary>
        /// <param name="nodeId">The id of the node to get the queue for</param>
        /// <returns>The initilaizes event queue for the single subscriber</returns>
        AsyncQueue<ChangeEvent> Subscribe(ICachePeer peer);

        /// <summary>
        /// Detatches a subscriber from the event queue
        /// </summary>
        /// <param name="nodeId">The id of the nede to detach</param>
        void Unsubscribe(ICachePeer peer);

        /// <summary>
        /// Purges all stale subcriber nodes
        /// </summary>
        void PurgeStaleSubscribers();
    }
}
