/*
* Copyright (c) 2022 Vaughn Nugent
* 
* Library: VNLib
* Package: VNLib.Data.Caching.ObjectCache
* File: ObjectCacheStore.cs 
*
* ObjectCacheStore.cs is part of VNLib.Data.Caching.ObjectCache which is part of the larger 
* VNLib collection of libraries and utilities.
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

using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;

using VNLib.Utils.Async;
using VNLib.Utils.Memory;
using VNLib.Utils.Logging;
using VNLib.Utils.Extensions;
using VNLib.Net.Messaging.FBM.Server;
using static VNLib.Data.Caching.Constants;


#pragma warning disable CA1849 // Call async methods when in an async method

namespace VNLib.Data.Caching.ObjectCache
{
    public delegate ReadOnlySpan<byte> GetBodyDataCallback<T>(T state);

    /// <summary>
    /// A <see cref="FBMListener"/> implementation of a <see cref="CacheListener"/>
    /// </summary>
    public class ObjectCacheStore : FBMListenerBase, IDisposable
    {
        private readonly SemaphoreSlim StoreLock;
        private bool disposedValue;

        ///<inheritdoc/>
        protected override ILogProvider Log { get; }

        /// <summary>
        /// A queue that stores update and delete events
        /// </summary>
        public AsyncQueue<ChangeEvent> EventQueue { get; }

        /// <summary>
        /// The Cache store to access data blobs
        /// </summary>
        private readonly BlobCache Cache;

        private readonly IUnmangedHeap Heap;


        /// <summary>
        /// Initialzies a new <see cref="ObjectCacheStore"/>
        /// </summary>
        /// <param name="dir">The <see cref="DirectoryInfo"/> to store blob files to</param>
        /// <param name="cacheMax"></param>
        /// <param name="log"></param>
        /// <param name="heap"></param>
        /// <param name="singleReader">A value that indicates if a single thread is processing events</param>
        public ObjectCacheStore(int cacheMax, ILogProvider log, IUnmangedHeap heap, bool singleReader)
        {
            Log = log;
            //We can use a single writer and single reader in this context
            EventQueue = new(true, singleReader);
            Cache = new(cacheMax);
            Heap = heap;
            InitListener(heap);
            StoreLock = new(1,1);
        }

        ///<inheritdoc/>
        protected override Task ProcessAsync(FBMContext context, object? userState, CancellationToken exitToken)
        {
            try
            {
                //Get the action header
                string action = context.Method();

                //Optional newid header
                string? alternateId = context.NewObjectId();

                switch (action)
                {
                    case Actions.Get:
                        {
                            //Get the object-id header
                            string objectId = context.ObjectId();

                            //Try read sync
                            if (StoreLock.Wait(0))
                            {
                                try
                                {
                                    UnsafeReadEntry(context, objectId);
                                }
                                finally
                                {
                                    StoreLock.Release();
                                }

                                return Task.CompletedTask;
                            }
                            else
                            {
                                //Read entry async
                                return InternalReadEntryAsync(context, objectId, exitToken);
                            }
                        }
                        
                    case Actions.AddOrUpdate:
                        {
                            //Get the object-id header
                            string objectId = context.ObjectId();

                            //Create change event for the object
                            ChangeEvent change = new(objectId, alternateId, false);

                            //Attempt to aquire lock sync
                            if (StoreLock.Wait(0))
                            {
                                //aquired sync
                                try
                                {
                                    //Update the item
                                    UnsafeAddOrUpdate(objectId, alternateId, GetBodyData, context);
                                }
                                finally
                                {
                                    StoreLock.Release();
                                }

                                //Add to event queue
                                EnqueEvent(change);

                                //Set status code
                                context.CloseResponse(ResponseCodes.Okay);

                                return Task.CompletedTask;
                            }
                            else
                            {
                                //Lock will be awaited async and 
                                return InternalAddOrUpdateAsync(context, change, exitToken);
                            }
                        }
                    case Actions.Delete:
                        {
                            //Get the object-id header
                            string objectId = context.ObjectId();

                            //Create change event
                            ChangeEvent change = new(objectId, alternateId, true);

                            //See if lock can be entered without waiting
                            if (StoreLock.Wait(0))
                            {
                                bool found = false;

                                try
                                {
                                    //Sync
                                    found = UnsafeDeleteEntry(objectId);
                                }
                                finally
                                {
                                    StoreLock.Release();
                                }

                                //Notify change 
                                EnqueEvent(change);

                                //Set status code if found
                                context.CloseResponse(found ? ResponseCodes.Okay : ResponseCodes.NotFound);

                                return Task.CompletedTask;
                            }
                            else
                            {
                                //lock will yeild async
                                return InternalDeleteAsync(context, change, exitToken);
                            }
                        }
                    // event queue dequeue request
                    case Actions.Dequeue:
                        {
                            static void SetResponse(ChangeEvent change, FBMContext context)
                            {
                                if (change.Deleted)
                                {
                                    context.CloseResponse("deleted");
                                    context.Response.WriteHeader(ObjectId, change.CurrentId);
                                }
                                else
                                {
                                    //Changed
                                    context.CloseResponse("modified");
                                    context.Response.WriteHeader(ObjectId, change.CurrentId);

                                    //Set old id if an old id is set
                                    if (change.CurrentId != null)
                                    {
                                        context.Response.WriteHeader(NewObjectId, change.AlternateId);
                                    }
                                }
                            }

                            static async Task DequeAsync(AsyncQueue<ChangeEvent> queue, FBMContext context, CancellationToken exitToken)
                            {
                                //Wait for a new message to process
                                ChangeEvent ev = await queue.DequeueAsync(exitToken);

                                //Set the response
                                SetResponse(ev, context);
                            }

                            //If no event bus is registered, then this is not a legal command
                            if (userState is not AsyncQueue<ChangeEvent> eventBus)
                            {
                                context.CloseResponse(ResponseCodes.NotFound);

                                return Task.CompletedTask;
                            }

                            //try to deq without awaiting
                            if (eventBus.TryDequeue(out ChangeEvent? change))
                            {
                                SetResponse(change, context);

                                return Task.CompletedTask;
                            }
                            else
                            {
                                //Process async
                                return DequeAsync(eventBus, context, exitToken);
                            }
                        }
                       
                }

                Log.Error("Unhandled cache event!");
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch(Exception ex)
            {
                //Log error and set error status code
                Log.Error(ex);
                context.CloseResponse(ResponseCodes.Error);
            }

            return Task.CompletedTask;
        }
        
        
        private static ReadOnlySpan<byte> GetBodyData(FBMContext ctx) => ctx.Request.BodyData;

        private void EnqueEvent(ChangeEvent change)
        {
            if (!EventQueue.TryEnque(change))
            {
                Log.Warn("Change event {ev} was not enqued because the event queue is overflowing!", change.CurrentId);
            }
        }

        private void UnsafeReadEntry(FBMContext context, string objectId)
        {
            if (Cache!.TryGetValue(objectId, out CacheEntry data))
            {
                //Set the status code and write the buffered data to the response buffer
                context.CloseResponse(ResponseCodes.Okay);

                //Copy data to response buffer
                context.Response.WriteBody(data.GetDataSegment());
            }
            else
            {
                context.CloseResponse(ResponseCodes.NotFound);
            }
        }

        async Task InternalReadEntryAsync(FBMContext context, string objectId, CancellationToken cancellation)
        {
            //enter lock async
            using SemSlimReleaser rel = await StoreLock.GetReleaserAsync(cancellation);

            UnsafeReadEntry(context, objectId);
        }

        private async Task InternalAddOrUpdateAsync(FBMContext context, ChangeEvent change, CancellationToken cancellation)
        {
            //Wait for lock since we know it will yeild async
            using (SemSlimReleaser rel = await StoreLock.GetReleaserAsync(cancellation))
            {
                UnsafeAddOrUpdate(change.CurrentId, change.AlternateId, GetBodyData, context);
            }

            //Add to event queue
            EnqueEvent(change);

            //Set status code
            context.CloseResponse(ResponseCodes.Okay);
        }
      
        private void UnsafeAddOrUpdate<T>(string objectId, string? alternateId, GetBodyDataCallback<T> bodyData, T state)
        {
            CacheEntry entry;

            //See if new/alt session id was specified
            if (string.IsNullOrWhiteSpace(alternateId))
            {
                //See if blob exists
                if (!Cache!.TryGetValue(objectId, out entry))
                {
                    //Create the new cache entry since it does not exist
                    entry = CacheEntry.Create(bodyData(state), Heap);

                    //Add to cache
                    Cache.Add(objectId, entry);
                }
                else
                {
                    //Reset the buffer state
                    entry.UpdateData(bodyData(state));
                }
            }
            //Need to change the id of the record
            else
            {
                //Try to change the blob key
                if (!Cache!.TryChangeKey(objectId, alternateId, out entry))
                {
                    //Create the new cache entry since it does not exist
                    entry = CacheEntry.Create(bodyData(state), Heap);

                    //Add to cache by its alternate id
                    Cache.Add(alternateId, entry);
                }
                else
                {
                    //Reset the buffer state
                    entry.UpdateData(bodyData(state));
                }
            }

            //Update modified time to current utc time
            entry.SetTime(DateTime.UtcNow);
        }

        private async Task InternalDeleteAsync(FBMContext context, ChangeEvent change, CancellationToken cancellation)
        {
            bool found = false;

            //enter the lock
            using(SemSlimReleaser rel = await StoreLock.GetReleaserAsync(cancellation))
            {
                //Sync
                found = UnsafeDeleteEntry(change.CurrentId);
            }

            //Notify change 
            EnqueEvent(change);

            //Set status code if found
            context.CloseResponse(found ? ResponseCodes.Okay : ResponseCodes.NotFound);
        }

        private bool UnsafeDeleteEntry(string id) => Cache!.Remove(id);


        /// <summary>
        /// Asynchronously adds or updates an object in the store and optionally update's its id
        /// </summary>
        /// <param name="objectId">The current (or old) id of the object</param>
        /// <param name="alternateId">An optional id to update the blob to</param>
        /// <param name="bodyData">A callback that returns the data for the blob</param>
        /// <param name="state">The state parameter to pass to the data callback</param>
        /// <param name="token">A token to cancel the async operation</param>
        /// <returns>A value task that represents the async operation</returns>
        public async ValueTask AddOrUpdateBlobAsync<T>(string objectId, string? alternateId, GetBodyDataCallback<T> bodyData, T state, CancellationToken token = default)
        {
            //Test the lock before waiting async
            if (!StoreLock.Wait(0))
            {
                //Wait async to avoid task alloc
                await StoreLock.WaitAsync(token);
            }
            try
            {
                UnsafeAddOrUpdate(objectId, alternateId, bodyData, state);
            }
            finally
            {
                StoreLock.Release();
            }
        }

        /// <summary>
        /// Asynchronously deletes a previously stored item
        /// </summary>
        /// <param name="id">The id of the object to delete</param>
        /// <param name="token">A token to cancel the async lock await</param>
        /// <returns>A task that completes when the item has been deleted</returns>
        public async ValueTask<bool> DeleteItemAsync(string id, CancellationToken token = default)
        {
            //Test the lock before waiting async
            if (!StoreLock.Wait(0))
            {
                //Wait async to avoid task alloc
                await StoreLock.WaitAsync(token);
            }
            try
            {
                return UnsafeDeleteEntry(id);
            }
            finally
            {
                StoreLock.Release();
            }
        }


        ///<inheritdoc/>
        protected virtual void Dispose(bool disposing)
        {
            if (!disposedValue)
            {
                if (disposing)
                {
                    Cache?.Clear();
                }
                
                StoreLock.Dispose();
                
                disposedValue = true;
            }
        }
        
        ///<inheritdoc/>
        public void Dispose()
        {
            // Do not change this code. Put cleanup code in 'Dispose(bool disposing)' method
            Dispose(disposing: true);
            GC.SuppressFinalize(this);
        }
    }
}
