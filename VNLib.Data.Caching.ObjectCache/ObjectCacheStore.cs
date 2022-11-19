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
* it under the terms of the GNU General Public License as published
* by the Free Software Foundation, either version 2 of the License,
* or (at your option) any later version.
*
* VNLib.Data.Caching.ObjectCache is distributed in the hope that it will be useful,
* but WITHOUT ANY WARRANTY; without even the implied warranty of
* MERCHANTABILITY or FITNESS FOR A PARTICULAR PURPOSE. See the GNU 
* General Public License for more details.
*
* You should have received a copy of the GNU General Public License 
* along with VNLib.Data.Caching.ObjectCache. If not, see http://www.gnu.org/licenses/.
*/

using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;

using VNLib.Utils.IO;
using VNLib.Utils.Async;
using VNLib.Utils.Memory;
using VNLib.Utils.Logging;
using VNLib.Utils.Extensions;
using VNLib.Net.Messaging.FBM.Server;
using static VNLib.Data.Caching.Constants;

namespace VNLib.Data.Caching.ObjectCache
{
    public delegate ReadOnlySpan<byte> GetBodyDataCallback<T>(T state);

    /// <summary>
    /// A <see cref="FBMListener"/> implementation of a <see cref="CacheListener"/>
    /// </summary>
    public class ObjectCacheStore : CacheListener, IDisposable
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
        /// Initialzies a new <see cref="ObjectCacheStore"/>
        /// </summary>
        /// <param name="dir">The <see cref="DirectoryInfo"/> to store blob files to</param>
        /// <param name="cacheMax"></param>
        /// <param name="log"></param>
        /// <param name="heap"></param>
        /// <param name="singleReader">A value that indicates if a single thread is processing events</param>
        public ObjectCacheStore(DirectoryInfo dir, int cacheMax, ILogProvider log, IUnmangedHeap heap, bool singleReader)
        {
            Log = log;
            //We can use a single writer and single reader in this context
            EventQueue = new(true, singleReader);
            InitCache(dir, cacheMax, heap);
            InitListener(heap);
            StoreLock = new(1,1);
        }

        ///<inheritdoc/>
        protected override async Task ProcessAsync(FBMContext context, object? userState, CancellationToken cancellationToken)
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
                            //Take lock on store 
                            using SemSlimReleaser rel = await StoreLock.GetReleaserAsync(cancellationToken: cancellationToken);
                            if (Cache!.TryGetValue(objectId, out MemoryHandle<byte>? data))
                            {
                                //Set the status code and write the buffered data to the response buffer
                                context.CloseResponse(ResponseCodes.Okay);
                                //Copy data to response buffer
                                context.Response.WriteBody(data.Span);
                            }
                            else
                            {
                                context.CloseResponse(ResponseCodes.NotFound);
                            }
                        }
                        break;
                    case Actions.AddOrUpdate:
                        {
                            //Get the object-id header
                            string objectId = context.ObjectId();
                            //Add/update a blob async
                            await AddOrUpdateBlobAsync(objectId, alternateId, static context => context.Request.BodyData, context);
                            //Notify update the event bus
                            await EventQueue.EnqueueAsync(new(objectId, alternateId, false), cancellationToken);
                            //Set status code
                            context.CloseResponse(ResponseCodes.Okay);                           
                        }
                        break;
                    case Actions.Delete:
                        {
                            //Get the object-id header
                            string objectId = context.ObjectId();
                           
                            if (await DeleteItemAsync(objectId))
                            {
                                //Notify deleted
                                await EventQueue.EnqueueAsync(new(objectId, null, true), cancellationToken);
                                //Set status header
                                context.CloseResponse(ResponseCodes.Okay);
                            }
                            else
                            {
                                //Set status header
                                context.CloseResponse(ResponseCodes.NotFound);
                            }
                        }
                        break;
                    // event queue dequeue request
                    case Actions.Dequeue:
                        {
                            //If no event bus is registered, then this is not a legal command
                            if (userState is not AsyncQueue<ChangeEvent> eventBus)
                            {
                                context.CloseResponse(ResponseCodes.NotFound);
                                break;
                            }
                            //Wait for a new message to process
                            ChangeEvent ev = await eventBus.DequeueAsync(cancellationToken);
                            if (ev.Deleted)
                            {
                                context.CloseResponse("deleted");
                                context.Response.WriteHeader(ObjectId, ev.CurrentId);
                            }
                            else
                            {
                                //Changed
                                context.CloseResponse("modified");
                                context.Response.WriteHeader(ObjectId, ev.CurrentId);
                                //Set old id if an old id is set
                                if (ev.CurrentId != null)
                                {
                                    context.Response.WriteHeader(NewObjectId, ev.AlternateId);
                                }
                            }
                        }
                        break;
                }
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

        /// <summary>
        /// Asynchronously deletes a previously stored item
        /// </summary>
        /// <param name="id">The id of the object to delete</param>
        /// <returns>A task that completes when the item has been deleted</returns>
        public async Task<bool> DeleteItemAsync(string id)
        {
            using SemSlimReleaser rel = await StoreLock.GetReleaserAsync();
            return Cache!.Remove(id);
        }
        
        /// <summary>
        /// Asynchronously adds or updates an object in the store and optionally update's its id
        /// </summary>
        /// <param name="objectId">The current (or old) id of the object</param>
        /// <param name="alternateId">An optional id to update the blob to</param>
        /// <param name="bodyData">A callback that returns the data for the blob</param>
        /// <param name="state">The state parameter to pass to the data callback</param>
        /// <returns></returns>
        public async Task AddOrUpdateBlobAsync<T>(string objectId, string? alternateId, GetBodyDataCallback<T> bodyData, T state)
        {
            MemoryHandle<byte>? blob;
            //See if new/alt session id was specified
            if (string.IsNullOrWhiteSpace(alternateId))
            {
                //Take lock on store 
                using SemSlimReleaser rel = await StoreLock.GetReleaserAsync();
                //See if blob exists
                if (!Cache!.TryGetValue(objectId, out blob))
                {
                    //If not, create new blob and add to store
                    blob = Heap.AllocAndCopy(bodyData(state));
                    Cache.Add(objectId, blob);
                }
                else
                {
                    //Reset the buffer state
                    blob.WriteAndResize(bodyData(state));
                }
            }
            //Need to change the id of the record
            else
            {
                //Take lock on store 
                using SemSlimReleaser rel = await StoreLock.GetReleaserAsync();
                //Try to change the blob key
                if (!Cache!.TryChangeKey(objectId, alternateId, out blob))
                {
                    //Blob not found, create new blob
                    blob = Heap.AllocAndCopy(bodyData(state));
                    Cache.Add(alternateId, blob);
                }
                else
                {
                    //Reset the buffer state
                    blob.WriteAndResize(bodyData(state));
                }
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
