/*
* Copyright (c) 2026 Vaughn Nugent
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

namespace VNLib.Data.Caching
{
    /// <summary>
    /// Exports extension methods for the <see cref="ICacheClient"/> interface
    /// </summary>
    public static class CacheClientExtensions
    {
        /// <inheritdoc cref="ICacheClient.GetAsync{T}(string, System.Buffers.ReadOnlySpanAction{byte, T}, T, CancellationToken)"/>
        /// <param name="rawData">An object that uses <see cref="IObjectData.SetData(ReadOnlySpan{byte})"/> to write the item data</param>
        /// <exception cref="ArgumentNullException">If the supplied cache object, or <paramref name="rawData"/> is null</exception>
        public static Task GetAsync(
            this ICacheClient cache, 
            string key, 
            IObjectData rawData,
            CancellationToken cancellation
        )
        {
            ArgumentNullException.ThrowIfNull(cache);
            ArgumentNullException.ThrowIfNull(rawData);
            return cache.GetAsync(
                key, 
                callback: static (data, cd) => cd.SetData(data), 
                state: rawData, 
                cancellation
            );
        }

        /// <inheritdoc cref="ICacheClient.AddOrUpdateAsync{T}(string, string?, ObjectDataGet{T}, T, CancellationToken)"/>
        /// <param name="rawData">An object that uses <see cref="IObjectData.GetData"/> to retrieve the item data</param>
        /// <exception cref="ArgumentNullException">If the supplied cache object, or <paramref name="rawData"/> is null</exception>
        public static Task AddOrUpdateAsync(
            this ICacheClient cache, 
            string key, 
            string? newKey, 
            IObjectData rawData, 
            CancellationToken cancellation
        )
        {
            ArgumentNullException.ThrowIfNull(cache);
            ArgumentNullException.ThrowIfNull(rawData);
            return cache.AddOrUpdateAsync(
                key, 
                newKey, 
                callback: static cd => cd.GetData(), 
                state: rawData, 
                cancellation
            );
        }

        /// <inheritdoc cref="ICacheClient.AddOrUpdateAsync{T}(string, string?, ObjectDataGet{T}, T, CancellationToken)"/>       
        /// <param name="rawData">A <see cref="ReadOnlyMemory{T}"/> block to write to the item</param>
        /// <exception cref="ArgumentNullException">If the supplied cache object</exception>
        public static Task AddOrUpdateAsync(
            this ICacheClient cache, 
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

        /// <inheritdoc cref="AddOrUpdateAsync(ICacheClient, string, string?, ReadOnlyMemory{byte}, CancellationToken)"/>
        /// <param name="rawData">A <see cref="Memory{T}"/> block to write to the item</param>
        public static Task AddOrUpdateAsync(
            this ICacheClient cache,
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

        /// <inheritdoc cref="AddOrUpdateAsync(ICacheClient, string, string?, ReadOnlyMemory{byte}, CancellationToken)"/>
        /// <param name="rawData">A byte array to write to the item</param>
        public static Task AddOrUpdateAsync(
            this ICacheClient cache,
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
        /// Gets an object from the server if it exists and transforms it using the <paramref name="getter"/>
        /// </summary>
        /// <typeparam name="TResult">The user defined result type</typeparam>
        /// <typeparam name="TState">The user state type to pass to the get function</typeparam>
        /// <param name="getter">A callback function that computes an object result from binary data</param>
        /// <param name="state">A user-state parameter to be passed back to the callback function</param>
        /// <inheritdoc cref="ICacheClient.GetAsync{T}(string, System.Buffers.ReadOnlySpanAction{byte, T}, T, CancellationToken)"/>
        public static async Task<TResult?> GetAsync<TResult, TState>(
            this ICacheClient cache,
            string key,
            GetObjectFromData<TResult, TState> getter,
            TState state,
            CancellationToken cancellationToken = default
        )
        {
            ArgumentNullException.ThrowIfNull(cache);
            ArgumentNullException.ThrowIfNull(getter);

            //Get state will store the object result if successful get operation
            GetObjectState<TResult, TState> st = new(state, getter);

            //Get the object, if successful, compute the result
            await cache.GetAsync(
                key,
                callback: static (d, s) => s.ComputeResult(d),
                state: st,
                cancellationToken
            ).ConfigureAwait(false);

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
        public static Task<T?> GetAsync<T>(this ICacheClient cache, string key, CancellationToken cancellation)
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
        /// <returns>A task that completes when the update operation has completed</returns>
        public static Task AddOrUpdateAsync<T>(
            this ICacheClient cache, 
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
                serializer: cache.DefaultSerializer, 
                cancellation
            );
        }      
    }
}
