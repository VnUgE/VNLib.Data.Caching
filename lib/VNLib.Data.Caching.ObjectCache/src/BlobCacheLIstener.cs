﻿/*
* Copyright (c) 2023 Vaughn Nugent
* 
* Library: VNLib
* Package: VNLib.Data.Caching.ObjectCache
* File: BlobCacheListener.cs 
*
* BlobCacheListener.cs is part of VNLib.Data.Caching.ObjectCache which is part of the larger 
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


/*
 * The latest bucket based cache store relys on bucket level locking
 * to distribute locking across threads and reduce contention.
 * 
 * This design relys on holding the bucket lock for the entire duration
 * of the CacheEntry manipulation, its id, movment, and reading/writing
 * the entirie's contents.
 * 
 * Some drawbacks are the basics with key-derrived bucket systems:
 * bucket imbalance due to key distribtion. 
 * 
 * Design perfers average speed, but will need to be tested heavily per 
 * use-case.
 */

using System;
using System.Threading;
using System.Threading.Tasks;

using VNLib.Utils.Async;
using VNLib.Utils.Memory;
using VNLib.Utils.Logging;
using VNLib.Net.Messaging.FBM.Server;
using static VNLib.Data.Caching.Constants;

namespace VNLib.Data.Caching.ObjectCache
{
    public delegate ReadOnlySpan<byte> GetBodyDataCallback<T>(T state);

    /// <summary>
    /// An <see cref="FBMListener"/> for key-value object data caching servers.
    /// </summary>
    public class BlobCacheListener : FBMListenerBase, IDisposable
    {
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
        public IBlobCacheTable Cache { get; }


        /// <summary>
        /// Initialzies a new <see cref="BlobCacheListener"/>
        /// </summary>
        /// <param name="cache">The cache table to work from</param>
        /// <param name="log">Writes error and debug logging information</param>
        /// <param name="heap">The heap to alloc FBM buffers and <see cref="CacheEntry"/> cache buffers from</param>
        /// <param name="singleReader">A value that indicates if a single thread is processing events</param>
        /// <exception cref="ArgumentNullException"></exception>
        public BlobCacheListener(IBlobCacheTable cache, ILogProvider log, IUnmangedHeap heap, bool singleReader)
        {
            Log = log;
            
            Cache = cache ?? throw new ArgumentNullException(nameof(cache));

            //Writes may happen from multple threads with bucket design and no lock
            EventQueue = new(false, singleReader);

            InitListener(heap);
        }

        ///<inheritdoc/>
        protected override async Task ProcessAsync(FBMContext context, object? userState, CancellationToken exitToken)
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

                            //Process read
                            await ReadEntryAsync(context, objectId, exitToken);
                            return;
                        }                        
                    case Actions.AddOrUpdate:
                        {
                            //Get the object-id header
                            string objectId = context.ObjectId();

                            //Create change event for the object
                            ChangeEvent change = new(objectId, alternateId, false);

                            await AddOrUpdateAsync(context, change, exitToken);
                            return;
                        }
                    case Actions.Delete:
                        {
                            //Get the object-id header
                            string objectId = context.ObjectId();

                            //Create change event
                            ChangeEvent change = new(objectId, alternateId, true);

                            await DeleteEntryAsync(context, change, exitToken);
                            return;
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

                                return;
                            }

                            //try to deq without awaiting
                            if (eventBus.TryDequeue(out ChangeEvent? change))
                            {
                                SetResponse(change, context);
                            }
                            else
                            {
                                //Process async
                                await DequeAsync(eventBus, context, exitToken);
                            }

                            return;
                        }
                       
                }

                Log.Error("Unhandled cache event for session {id}", context.Request.ConnectionId);
                context.CloseResponse(ResponseCodes.Error);
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
        }
        
        private async ValueTask ReadEntryAsync(FBMContext context, string objectId, CancellationToken cancellation)
        {
            //Try to get the bucket that the id should belong to
            IBlobCacheBucket bucket = Cache.GetBucket(objectId);

            //Wait for lock on bucket async
            using CacheBucketHandle handle = await bucket.WaitAsync(cancellation);

            if (handle.Cache.TryGetValue(objectId, out CacheEntry data))
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

        private async ValueTask DeleteEntryAsync(FBMContext context, ChangeEvent change, CancellationToken cancellation)
        {
            //Remove the object from the blob store
            bool found = await Cache.DeleteObjectAsync(change.CurrentId, cancellation);

            context.CloseResponse(found ? ResponseCodes.Okay : ResponseCodes.NotFound);

            //Enque change if item was successfully deleted
            if (found)
            {
                EnqueEvent(change);
            }
        }

        private async ValueTask AddOrUpdateAsync(FBMContext context, ChangeEvent change, CancellationToken cancellation)
        {
            //Run add/update and get the valuetask
            await Cache.AddOrUpdateObjectAsync(change.CurrentId, change.AlternateId, static r => r.BodyData, context.Request, default, cancellation);

            EnqueEvent(change);

            context.CloseResponse(ResponseCodes.Okay);
        }

        private void EnqueEvent(ChangeEvent change)
        {
            if (!EventQueue.TryEnque(change))
            {
                Log.Warn("Change event {ev} was not enqued because the event queue is overflowing!", change.CurrentId);
            }
        }


        ///<inheritdoc/>
        protected virtual void Dispose(bool disposing)
        {
            if (!disposedValue)
            {
                Cache.Dispose();
                
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