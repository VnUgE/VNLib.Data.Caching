/*
* Copyright (c) 2025 Vaughn Nugent
* 
* Library: VNLib
* Package: VNLib.Data.Caching
* File: GlobalCacheExtensions.cs 
*
* GlobalCacheExtensions.cs is part of VNLib.Data.Caching which is part of the larger 
* VNLib collection of libraries and utilities.
*
* VNLib.Data.Caching is free software: you can redistribute it and/or modify 
* it under the terms of the GNU Affero General Public License as 
* published by the Free Software Foundation, either version 3 of the
* License, or (at your option) any later version.
*
* VNLib.Data.Caching is distributed in the hope that it will be useful,
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
using System.Runtime.CompilerServices;

using VNLib.Net.Messaging.FBM;
using VNLib.Data.Caching.Exceptions;

namespace VNLib.Data.Caching
{
    /// <summary>
    /// Exports extension methods for the <see cref="IGlobalCacheProvider"/> interface
    /// </summary>
    public static class GlobalCacheExtensions
    {
        /// <summary>
        /// Asynchronously gets a value from the backing cache store and writes it to the 
        /// supplied data buffer
        /// </summary>
        /// <param name="cache"></param>
        /// <param name="key">The key identifying the object to recover from cache</param>
        /// <param name="rawData">The </param>
        /// <param name="cancellation">A token to cancel the async operation</param>
        /// <returns>A task that complets when the object data has been written to the data buffer</returns>
        public static Task GetAsync(
            this IGlobalCacheProvider cache, 
            string key, 
            IObjectData rawData,
            CancellationToken cancellation
        )
        {
            ArgumentNullException.ThrowIfNull(cache);
            return cache.GetAsync(
                key, 
                callback: static (cd, data) => cd.SetData(data), 
                state: rawData, 
                cancellation
            );
        }

        /// <summary>
        /// Asynchronously sets (or updates) a cached value in the backing cache store 
        /// from the supplied raw data
        /// </summary>
        /// <param name="cache"></param>
        /// <param name="key">The key identifying the object to recover from cache</param>
        /// <param name="newKey">An optional key that will be changed for the new object</param>
        /// <param name="cancellation">A token to cancel the async operation</param>
        /// <param name="rawData">The raw data to store at the given key</param>
        /// <returns>A task that completes when the update operation has compelted</returns>
        public static Task AddOrUpdateAsync(
            this IGlobalCacheProvider cache, 
            string key, 
            string? newKey, 
            IObjectData rawData, 
            CancellationToken cancellation
        )
        {
            ArgumentNullException.ThrowIfNull(cache);
            return cache.AddOrUpdateAsync(
                key, 
                newKey, 
                callback: static cd => cd.GetData(), 
                state: rawData, 
                cancellation
            );
        }

        /// <summary>
        /// Asynchronously sets (or updates) a cached value in the backing cache store 
        /// from the supplied raw data
        /// </summary>
        /// <param name="cache"></param>
        /// <param name="key">The key identifying the object to recover from cache</param>
        /// <param name="newKey">An optional key that will be changed for the new object</param>
        /// <param name="cancellation">A token to cancel the async operation</param>
        /// <param name="rawData">The raw data to store at the given key</param>
        /// <returns>A task that completes when the update operation has compelted</returns>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static Task AddOrUpdateAsync(
            this IGlobalCacheProvider cache, 
            string key, 
            string? newKey, 
            ReadOnlyMemory<byte> rawData, 
            CancellationToken cancellation
        )
        {
            ArgumentNullException.ThrowIfNull(cache);
            return cache.AddOrUpdateAsync(
                key, 
                newKey, 
                callback: static cd => cd.Span, 
                state: rawData, 
                cancellation
            );
        }

        /// <summary>
        /// Asynchronously sets (or updates) a cached value in the backing cache store 
        /// from the supplied raw data
        /// </summary>
        /// <param name="cache"></param>
        /// <param name="key">The key identifying the object to recover from cache</param>
        /// <param name="newKey">An optional key that will be changed for the new object</param>
        /// <param name="cancellation">A token to cancel the async operation</param>
        /// <param name="rawData">The raw data to store at the given key</param>
        /// <returns>A task that completes when the update operation has compelted</returns>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static Task AddOrUpdateAsync(
            this IGlobalCacheProvider cache,
            string key,
            string? newKey,
            Memory<byte> rawData,
            CancellationToken cancellation
        )
        {
            ArgumentNullException.ThrowIfNull(cache);
            return AddOrUpdateAsync(
                cache, 
                key, 
                newKey, 
                (ReadOnlyMemory<byte>)rawData, 
                cancellation
            );
        }

        /// <summary>
        /// Asynchronously sets (or updates) a cached value in the backing cache store 
        /// from the supplied raw data
        /// </summary>
        /// <param name="cache"></param>
        /// <param name="key">The key identifying the object to recover from cache</param>
        /// <param name="newKey">An optional key that will be changed for the new object</param>
        /// <param name="cancellation">A token to cancel the async operation</param>
        /// <param name="rawData">The raw data to store at the given key</param>
        /// <returns>A task that completes when the update operation has compelted</returns>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static Task AddOrUpdateAsync(
            this IGlobalCacheProvider cache,
            string key,
            string? newKey,
            byte[] rawData,
            CancellationToken cancellation
        )
        {
            ArgumentNullException.ThrowIfNull(cache);
            return cache.AddOrUpdateAsync(
                key, 
                newKey, 
                callback: static cd => cd.AsSpan(), 
                state: rawData, 
                cancellation
            );
        }

        /// <summary>
        /// Gets an object from the server if it exists
        /// </summary>
        /// <typeparam name="T"></typeparam>
        /// <typeparam name="TState"></typeparam>
        /// <param name="cache"></param>
        /// <param name="objectId">The id of the object to get</param>
        /// <param name="cancellationToken">A token to cancel the operation</param>
        /// <param name="getter">A callback function that computes an object result from binary data</param>
        /// <param name="state">A user-state parameter to be passed back to the callback function</param>
        /// <returns>A task that completes to return the results of the response payload</returns>
        /// <exception cref="ArgumentException"></exception>
        /// <exception cref="ArgumentNullException"></exception>
        /// <exception cref="InvalidStatusException"></exception>
        /// <exception cref="ObjectDisposedException"></exception>
        /// <exception cref="InvalidResponseException"></exception>
        public static async Task<T?> GetAsync<T, TState>(
            this IGlobalCacheProvider cache,
            string objectId,
            GetObjectFromData<T, TState> getter,
            TState state,
            CancellationToken cancellationToken = default
        )
        {
            ArgumentNullException.ThrowIfNull(cache);
            ArgumentNullException.ThrowIfNull(getter);

            //Get state will store the object result if successfull get operation
            GetObjectState<T, TState> st = new(state, getter);

            //Get the object, if successfull, compute the result
            await cache.GetAsync(
                objectId, 
                callback: static (s, d) => s.ComputeResult(d), 
                state: st, 
                cancellationToken
            );

            //If the get operation failed, return a default value
            return st.Result;
        }


        /// <summary>
        /// Asynchronously gets a value from the backing cache store
        /// </summary>
        /// <typeparam name="T"></typeparam>
        /// <param name="cache"></param>
        /// <param name="key">The key identifying the object to recover from cache</param>
        /// <param name="cancellation">A token to cancel the async operation</param>
        /// <returns>The value if found, or null if it does not exist in the store</returns>
        public static Task<T?> GetAsync<T>(this IGlobalCacheProvider cache, string key, CancellationToken cancellation)
        {
            ArgumentNullException.ThrowIfNull(cache);
            return cache.GetAsync<T>(key, cache.DefaultDeserializer, cancellation);
        }

        /// <summary>
        /// Asynchronously sets (or updates) a cached value in the backing cache store
        /// </summary>
        /// <typeparam name="T"></typeparam>
        /// <param name="cache"></param>
        /// <param name="key">The key identifying the object to recover from cache</param>
        /// <param name="newKey">An optional key that will be changed for the new object</param>
        /// <param name="cancellation">A token to cancel the async operation</param>
        /// <param name="value">The value to set at the given key</param>
        /// <returns>A task that completes when the update operation has compelted</returns>
        public static Task AddOrUpdateAsync<T>(
            this IGlobalCacheProvider cache, 
            string key, 
            string? newKey, 
            T value, 
            CancellationToken cancellation
        )
        {
            ArgumentNullException.ThrowIfNull(cache);

            return cache.AddOrUpdateAsync(
                key, 
                newKey, 
                value, 
                serialzer: cache.DefaultSerializer, 
                cancellation
            );
        }      
    }
}