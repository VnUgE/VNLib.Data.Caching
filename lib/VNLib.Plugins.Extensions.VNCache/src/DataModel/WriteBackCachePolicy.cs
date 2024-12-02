/*
* Copyright (c) 2024 Vaughn Nugent
* 
* Library: VNLib
* Package: VNLib.Plugins.Extensions.VNCache
* File: WriteBackCachePolicy.cs 
*
* WriteBackCachePolicy.cs is part of VNLib.Plugins.Extensions.VNCache 
* which is part of the larger VNLib collection of libraries and utilities.
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
using System.Threading.Tasks;
using System.Diagnostics;

namespace VNLib.Plugins.Extensions.VNCache.DataModel
{
    /// <summary>
    /// Provides a policy for observing the completion of cache operations
    /// </summary>
    /// <param name="onFaulted">
    /// A callback function that is executed when a cache operation has fauled due to an exception
    /// </param>
    public class WriteBackCachePolicy(Action<Task> onFaulted) : ICacheTaskPolicy
    {
        ///<inheritdoc/>
        public Task ObserveOperationAsync(Task operation)
        {
            ArgumentNullException.ThrowIfNull(operation);

            if (!operation.IsCompleted)
            {
                //Defer the observation to the callback function to watch for errors
                _ = operation.ContinueWith(
                    ObserveOperation, 
                    cancellationToken: default,
                    TaskContinuationOptions.ExecuteSynchronously,
                    TaskScheduler.Default
                );
            }

            return Task.CompletedTask;
        }

        private void ObserveOperation(Task operation)
        {
            //Should only be called on a completed task
            Debug.Assert(operation.IsCompleted);

            if (operation.IsFaulted)
            {
                onFaulted(operation);
            }
        }
    }
   
}
