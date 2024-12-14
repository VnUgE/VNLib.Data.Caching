/*
* Copyright (c) 2024 Vaughn Nugent
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

using VNLib.Utils.Logging;
using VNLib.Net.Messaging.FBM.Server;
using static VNLib.Data.Caching.Constants;

namespace VNLib.Data.Caching.ObjectCache
{
    /// <summary>
    /// An <see cref="FBMListener"/> for key-value object data caching servers.
    /// </summary>
    /// <remarks>
    /// Initialzies a new <see cref="BlobCacheListener{T}"/>
    /// </remarks>
    /// <param name="cache">The cache table to work from</param>
    /// <param name="queue">The event queue to publish changes to</param>
    /// <param name="config">The listener configuration object</param>
    /// <exception cref="ArgumentNullException"></exception>
    public class BlobCacheListener<T>(IBlobCacheTable cache, BlobCacheListenerConfig config, ICacheListenerEventQueue<T> queue) 
        : FBMListenerBase<T>, IDisposable
    {
        private bool disposedValue;

        ///<inheritdoc/>
        protected override ILogProvider Log { get; } = config.Log;
        ///<inheritdoc/>
        protected override FBMListener Listener { get; } = new(config.MemoryManager);

        /// <summary>
        /// The configuration instance for the listener
        /// </summary>
        public BlobCacheListenerConfig Config { get; } = config ?? throw new ArgumentNullException(nameof(config));

        /// <summary>
        /// A queue that stores update and delete events
        /// </summary>
        public ICacheListenerEventQueue<T> EventQueue { get; } = queue ?? throw new ArgumentNullException(nameof(queue));

        /// <summary>
        /// The Cache store to access data blobs
        /// </summary>
        public IBlobCacheTable Cache { get; } = cache ?? throw new ArgumentNullException(nameof(cache));


        private readonly ILogProvider _tLog = config.LogTransactions ? config.Log : new NullLogger();

        ///<inheritdoc/>
        protected override async Task ProcessAsync(FBMContext context, T? userState, CancellationToken exitToken)
        {
            try
            {
                //Get the action header
                string? action = FBMCacheHeaders.GetMethod(context);
                string? objectId = FBMCacheHeaders.GetObjectId(context);
                string? alternateId = FBMCacheHeaders.GetNewObjectId(context);

                switch (action)
                {
                    case Actions.Get:
                        {
                            if (string.IsNullOrWhiteSpace(objectId))
                            {
                                break;
                            }

                            //Process read
                            await ReadEntryAsync(context, objectId, exitToken);

                            return;
                        }                        
                    case Actions.AddOrUpdate:
                        {
                            if (string.IsNullOrWhiteSpace(objectId))
                            {
                                break;
                            }

                            //Create change event for the object
                            ChangeEvent change = new(objectId, alternateId, deleted: false);

                            if (config.EnableMessageChecksums)
                            {
                                switch (context.Request.IsClientChecksumValid())
                                {
                                    //0 is checksum sent, supported, but invalid
                                    case 0:
                                        context.CloseResponse(ResponseCodes.InvalidChecksum);
                                        return;

                                    case -2:    //Method not supported, set an error header but allow the request
                                        context.Response.WriteHeader(ChecksumWarning, "Checksum method not supported");
                                        break;

                                    case 1:     //1 is checksum sent and valid
                                    case -1:    //No checksum sent
                                        break;
                                }
                            }

                            await AddOrUpdateAsync(context, change, exitToken);
                            return;
                        }
                    case Actions.Delete:
                        {
                            if (string.IsNullOrWhiteSpace(objectId))
                            {
                                break;
                            }

                            //Create change event
                            ChangeEvent change = new(objectId, alternateId, deleted: true);

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

                            //Determine if the queue is enabled for the user
                            if(!EventQueue.IsEnabled(userState))
                            {
                                context.CloseResponse(ResponseCodes.NotFound);
                                return;
                            }

                            //try to deq without awaiting
                            if (EventQueue.TryDequeue(userState, out ChangeEvent? change))
                            {
                                SetResponse(change, context);
                            }
                            else
                            {
                                //Wait for a new message to process
                                ChangeEvent ev = await EventQueue.DequeueAsync(userState, exitToken);

                                //Set the response
                                SetResponse(ev, context);
                            }

                            return;
                        }
                }

                Log.Debug("Unhandled cache event {action} for session {id}", action, context.Request.ConnectionId);
                context.CloseResponse(ResponseCodes.BadRequest);
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
                //Compute an fnv message checksum and send it to the client
                if (config.EnableMessageChecksums)
                {
                    FbmMessageChecksum.WriteFnv1aChecksum(context.Response, data.GetDataSegment());
                }

                //Set the status code and write the buffered data to the response buffer
                context.CloseResponse(ResponseCodes.Okay);
            
                context.Response.WriteBody(
                    data.GetDataSegment(), 
                    Net.Http.ContentType.Binary
                );
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

                _tLog.Debug("Deleted cache entry {id}", change.CurrentId);
            }
        }

        private async ValueTask AddOrUpdateAsync(FBMContext context, ChangeEvent change, CancellationToken cancellation)
        {
            //Run add/update and get the valuetask
            await Cache.AddOrUpdateObjectAsync(
                objectId: change.CurrentId, 
                alternateId: change.AlternateId, 
                static r => r.BodyData, 
                state: context.Request, 
                time: default, 
                cancellation
            );

            EnqueEvent(change);

            _tLog.Debug("Cache entry {id} added or updated. New ID {nid}", change.CurrentId, change.AlternateId);

            context.CloseResponse(ResponseCodes.Okay);
        }

        private void EnqueEvent(ChangeEvent change)
        {
            EventQueue.PublishEvent(change);
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

        sealed class NullLogger : ILogProvider
        {
            public void Flush()
            { }

            public object GetLogProvider() => null!;


            public bool IsEnabled(LogLevel level) => false;

            public void Write(LogLevel level, string value)
            { }

            public void Write(LogLevel level, Exception exception, string value = "")
            { }

            public void Write(LogLevel level, string value, params object?[] args)
            { }

            public void Write(LogLevel level, string value, params ValueType[] args)
            { }
        }
    }
}
