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

using System.Threading;
using System.Threading.Tasks;


namespace VNLib.Data.Caching.ObjectCache.Server
{
    /// <summary>
    /// Represents a queue of events for a specific peer node
    /// </summary>
    internal interface IPeerEventQueue
    {
        /// <summary>
        /// Dequeues an event from the queue asynchronously
        /// </summary>
        /// <param name="cancellation">A token to cancel the operation</param>
        /// <returns>The value task that represents the wait</returns>
        ValueTask<ChangeEvent> DequeueAsync(CancellationToken cancellation);
        
        /// <summary>
        /// Attemts to dequeue an event from the queue
        /// </summary>
        /// <param name="change">The change event that was dequeued if possible</param>
        /// <returns>True if the event was dequeued</returns>
        bool TryDequeue(out ChangeEvent change);
    }
}
