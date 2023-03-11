﻿/*
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
        internal static CacheEntry CreateEntry(this IBlobCache cache, string objectId, ReadOnlySpan<byte> initialData, DateTime time)
        {
            CacheEntry entry = CacheEntry.Create(initialData, cache.CacheHeap);
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
        /// Asynchronously adds or updates an object in the store and optionally update's it's id
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
            GetBodyDataCallback<T> bodyData,
            T state,
            DateTime time,
            CancellationToken cancellation = default)
        {
            //See if an id change is required
            if (string.IsNullOrWhiteSpace(alternateId))
            {
                //safe to get the bucket for the primary id
                IBlobCacheBucket bucket = table.GetBucket(objectId);

                //Wait for the bucket
                using CacheBucketHandle handle = await bucket.WaitAsync(cancellation);

                //add/update for single entity
                _ = handle.Cache.AddOrUpdateEntry(objectId, bodyData(state), time);
            }
            else
            {
                //Buckets for each id need to be obtained
                IBlobCacheBucket primary = table.GetBucket(objectId);
                IBlobCacheBucket alternate = table.GetBucket(alternateId);

                //Same bucket
                if (ReferenceEquals(primary, alternate))
                {
                    //wait for lock on only one bucket otherwise dealock
                    using CacheBucketHandle handle = await primary.WaitAsync(cancellation);

                    //Update the entry for the single bucket
                    _ = handle.Cache.TryChangeKey(objectId, alternateId, bodyData(state), time);
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
                            //Update the handle data and reuse the entry
                            entry.UpdateData(bodyData(state));

                            //Add the updated entry to the alternate table
                            alternateHandle.Cache.Add(alternateId, entry);
                        }
                        catch
                        {
                            //Cleanup handle if error adding
                            entry.Dispose();
                            throw;
                        }
                    }
                    else
                    {
                        //Old entry did not exist, we need to create a new entry for the alternate bucket
                        _ = alternateHandle.Cache.CreateEntry(alternateId, bodyData(state), time);
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

            //Wait for lock on bucket async
            using CacheBucketHandle handle = await bucket.WaitAsync(cancellation);

            //Remove the object from the blob store
            return handle.Cache.Remove(objectId);
        }
    }
}