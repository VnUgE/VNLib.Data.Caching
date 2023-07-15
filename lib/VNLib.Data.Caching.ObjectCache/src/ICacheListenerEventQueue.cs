/*
* Copyright (c) 2023 Vaughn Nugent
* 
* Library: VNLib
* Package: VNLib.Data.Caching.ObjectCache
* File: ICacheListenerEventQueue.cs 
*
* ICacheListenerEventQueue.cs is part of VNLib.Data.Caching.ObjectCache which 
* is part of the larger VNLib collection of libraries and utilities.
*
* VNLib.Data.Caching.ObjectCache is free software: you can redistribute it and/or modify 
* it under the terms of the GNU Affero General Public License as 
* published by the Free Software Foundation, either version 3 of the
* License, or (at your option) any later version.
*
* VNLib.Data.Caching.ObjectCache is distributed in the hope that it will be useful,
* but WITHOUT ANY WARRANTY; without even the implied warranty of
* MERCHANTABILITY or FITNESS FOR A PARTICULAR PURPOSE. See the
* GNU Affero General Public License for more details.
*
* You should have received a copy of the GNU Affero General Public License
* along with this program.  If not, see https://www.gnu.org/licenses/.
*/


using System.Threading;
using System.Threading.Tasks;
using System.Diagnostics.CodeAnalysis;

namespace VNLib.Data.Caching.ObjectCache
{
    /// <summary>
    /// Represents a single client's event queue
    /// </summary>
    public interface ICacheListenerEventQueue
    {
        /// <summary>
        /// Determines if the queue is enabled for the given user state
        /// </summary>
        /// <param name="userState">The unique state of the connection</param>
        /// <returns>True if event queuing is enabled</returns>
        bool IsEnabled([NotNullWhen(true)] object? userState);

        /// <summary>
        /// Attempts to dequeue a single event from the queue without blocking
        /// </summary>
        /// <param name="userState">A user state object to associate with the wait operation</param>
        /// <param name="changeEvent">The dequeued event if successfully dequeued</param>
        /// <returns>True if an event was waiting and could be dequeued, false otherwise</returns>
        bool TryDequeue(object userState, out ChangeEvent changeEvent);

        /// <summary>
        /// Waits asynchronously for an event to be dequeued
        /// </summary>
        /// <param name="userState">A user state object to associate with the wait operation</param>
        /// <param name="cancellation">A token to cancel the wait operation</param>
        /// <returns>The <see cref="ChangeEvent"/> that as a result of the dequeue operation</returns>
        ValueTask<ChangeEvent> DequeueAsync(object userState, CancellationToken cancellation);

        /// <summary>
        /// Publishes an event to the queue
        /// </summary>
        /// <param name="changeEvent">The change event to publish</param>
        void PublishEvent(ChangeEvent changeEvent);
    }
}
