/*
* Copyright (c) 2023 Vaughn Nugent
* 
* Library: VNLib
* Package: VNLib.Data.Caching.ObjectCache
* File: BlobCacheExtensions.cs 
*
* BlobCacheExtensions.cs is part of VNLib.Data.Caching.ObjectCache which is part of the larger 
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
using System.Threading;
using System.Threading.Tasks;

namespace VNLib.Data.Caching.ObjectCache
{
    /// <summary>
    /// Provides blob cache extension methods
    /// </summary>
    public static class BlobCacheExtensions
    {

        /// <summary>
        /// Gets a <see cref="CacheBucketHandle"/> that holds an exclusive lock 
        /// for the current bucekt and holds a referrence to the stored
        /// <see cref="IBlobCache"/>
        /// </summary>
        /// <param name="bucket"></param>
        /// <param name="cancellation">A token to cancel the wait operation</param>
        /// <returns>A <see cref="CacheBucketHandle"/> that holds the <see cref="IBlobCache"/> referrence</returns>
        public static ValueTask<CacheBucketHandle> WaitAsync(this IBlobCacheBucket bucket, CancellationToken cancellation)
        {
            _ = bucket ?? throw new ArgumentNullException(nameof(bucket));

            //Try enter the bucket lock
            ValueTask<IBlobCache> cacheWait = bucket.ManualWaitAsync(cancellation);

            if (cacheWait.IsCompleted)
            {
                IBlobCache bucketHandle = cacheWait.GetAwaiter().GetResult();
                return new ValueTask<CacheBucketHandle>(new CacheBucketHandle(bucket, bucketHandle));
            }
            else
            {
                return GetHandleAsync(cacheWait, bucket);
            }           

            static async ValueTask<CacheBucketHandle> GetHandleAsync(ValueTask<IBlobCache> waitTask, IBlobCacheBucket bucket)
            {
                IBlobCache cache = await waitTask.ConfigureAwait(false);
                return new CacheBucketHandle(bucket, cache);
            }
        }

        internal static CacheEntry CreateEntry(this IBlobCache cache, string objectId, ReadOnlySpan<byte> initialData, DateTime time)
        {
            CacheEntry entry = CacheEntry.Create(initialData, cache.MemoryManager);
            try
            {
                //try to add the entry, but if exists, let it throw
                cache.Add(objectId, entry);
                entry.SetTime(time);
                return entry;
            }
            catch
            {
                entry.Dispose();
                throw;
            }
        }

        internal static CacheEntry AddOrUpdateEntry(this IBlobCache cache, string objectId, ReadOnlySpan<byte> data, DateTime time)
        {
            //See if blob exists
            if (cache.TryGetValue(objectId, out CacheEntry entry))
            {
                //Update the entry since it exists
                entry.UpdateData(data);

                entry.SetTime(time);
            }
            else
            {
                //Create the new entry 
                entry = cache.CreateEntry(objectId, data, time);
            }

            return entry;
        }

        internal static CacheEntry TryChangeKey(this IBlobCache cache, string objectId, string alternateId, ReadOnlySpan<byte> data, DateTime time)
        { 
            //Change the key of the blob item and update its data
            if (cache.TryChangeKey(objectId, alternateId, out CacheEntry entry))
            {
                //If date is 0 length do not overwrite the old entry if found
                if (data.IsEmpty)
                {
                    return entry;
                }

                //Otherwise update the entry
                entry.UpdateData(data);
                entry.SetTime(time);

                return entry;
            }
            else
            {
                //entry does not exist at the old id, so we can create a new one at the alternate id
                return cache.CreateEntry(objectId, data, time);
            }
        }


        /// <summary>
        /// Asynchronously adds or updates an object in the store and optionally update's it's id.
        /// If the alternate key already exists, it's data is overwritten.
        /// </summary>
        /// <param name="table"></param>
        /// <param name="objectId">The current (or old) id of the object</param>
        /// <param name="alternateId">An optional id to update the blob to</param>
        /// <param name="bodyData">A callback that returns the data for the blob</param>
        /// <param name="state">The state parameter to pass to the data callback</param>
        /// <param name="time">The time to set on the cache record</param>
        /// <param name="cancellation">A token to cancel the async operation</param>
        /// <returns>A value task that represents the async operation</returns>
        public static async ValueTask AddOrUpdateObjectAsync<T>(
            this IBlobCacheTable table,
            string objectId,
            string? alternateId,
            ObjectDataReader<T> bodyData,
            T state,
            DateTime time,
            CancellationToken cancellation = default)
        {

            _ = table ?? throw new ArgumentNullException(nameof(table));
            _ = bodyData ?? throw new ArgumentNullException(nameof(bodyData));

            //See if an id change is required
            if (string.IsNullOrWhiteSpace(alternateId))
            {
                //safe to get the bucket for the primary id
                IBlobCacheBucket bucket = table.GetBucket(objectId);

                //Wait for the bucket to be available
                IBlobCache cache = await bucket.ManualWaitAsync(cancellation);

                try
                {
                    _ = cache.AddOrUpdateEntry(objectId, bodyData(state), time);
                }
                finally
                {
                    bucket.Release();
                }
            }
            else
            {
                //Buckets for each id need to be obtained
                IBlobCacheBucket primary = table.GetBucket(objectId);
                IBlobCacheBucket alternate = table.GetBucket(alternateId);

                //Same bucket
                if (ReferenceEquals(primary, alternate))
                {
                    IBlobCache cache = await primary.ManualWaitAsync(cancellation);

                    try
                    {
                        //Update the entry for the single bucket
                        _ = cache.TryChangeKey(objectId, alternateId, bodyData(state), time);
                    }
                    finally
                    {
                        primary.Release();
                    }
                }
                else
                {
                    //Buckets are different must be awaited individually
                    using CacheBucketHandle primaryHandle = await primary.WaitAsync(cancellation);
                    using CacheBucketHandle alternateHandle = await alternate.WaitAsync(cancellation);

                    //Get the entry from the primary hande
                    if (primaryHandle.Cache.Remove(objectId, out CacheEntry entry))
                    {
                        try
                        {
                            //Try to see if the alternate key already exists
                            if (alternateHandle.Cache.TryGetValue(alternateId, out CacheEntry existing))
                            {
                                existing.UpdateData(bodyData(state));

                                //dispose the old entry since we don't need it
                                entry.Dispose();
                            }
                            else
                            {
                                //Update the entry buffer and reuse the entry
                                entry.UpdateData(bodyData(state));

                                //Add the updated entry to the alternate table
                                alternateHandle.Cache.Add(alternateId, entry);
                            }
                        }
                        catch
                        {
                            //Cleanup removed entry if error adding
                            entry.Dispose();
                            throw;
                        }
                    }
                    else
                    {
                        //Try to see if the alternate key already exists in the target store
                        if (alternateHandle.Cache.TryGetValue(alternateId, out CacheEntry existing))
                        {
                            //overwrite the existing entry data
                            existing.UpdateData(bodyData(state));
                        }
                        else
                        {
                            //Old entry did not exist, we need to create a new entry for the alternate bucket
                            _ = alternateHandle.Cache.CreateEntry(alternateId, bodyData(state), time);
                        }
                    }
                }
            }
        }

        /// <summary>
        /// Asynchronously deletes a previously stored item
        /// </summary>
        /// <param name="table"></param>
        /// <param name="objectId">The id of the object to delete</param>
        /// <param name="cancellation">A token to cancel the async lock await</param>
        /// <returns>A task that completes when the item has been deleted</returns>
        public static async ValueTask<bool> DeleteObjectAsync(this IBlobCacheTable table, string objectId, CancellationToken cancellation = default)
        {
            //Try to get the bucket that the id should belong to
            IBlobCacheBucket bucket = table.GetBucket(objectId);

            //Wait for the bucket
            IBlobCache cache = await bucket.ManualWaitAsync(cancellation);

            try
            {
                return cache.Remove(objectId);
            }
            finally
            {
                bucket.Release();
            }
        }
    }
}
