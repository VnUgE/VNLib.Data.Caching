﻿/*
* Copyright (c) 2023 Vaughn Nugent
* 
* Library: VNLib
* Package: VNLib.Data.Caching
* File: IGlobalCacheProvider.cs 
*
* IGlobalCacheProvider.cs is part of VNLib.Data.Caching which is part of the larger 
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
    /// A delegate method that will set the raw object data on the state object
    /// if data was found
    /// </summary>
    /// <typeparam name="T"></typeparam>
    /// <param name="state">The state passed to the original call</param>
    /// <param name="objectData">The raw data of the cached object</param>
    public delegate void ObjectDataSet<T>(T state, ReadOnlySpan<byte> objectData);

    /// <summary>
    /// A delegate method that will get the raw objet data from a state object
    /// </summary>
    /// <typeparam name="T"></typeparam>
    /// <param name="state">The state object passed to the caller</param>
    /// <returns>The raw object data to store in cache</returns>
    public delegate ReadOnlySpan<byte> ObjectDataReader<T>(T state);

    /// <summary>
    /// A global cache provider interface
    /// </summary>
    public interface IGlobalCacheProvider
    {
        /// <summary>
        /// Gets a value that indicates if the cache provider is currently available
        /// </summary>
        bool IsConnected { get; }

        /// <summary>
        /// Gets the underlying cache store object
        /// </summary>
        /// <returns>The underlying cache store instance</returns>
        object GetUnderlyingStore();

        /// <summary>
        /// Asynchronously gets a value from the backing cache store
        /// </summary>
        /// <typeparam name="T"></typeparam>
        /// <param name="key">The key identifying the object to recover from cache</param>
        /// <param name="cancellation">A token to cancel the async operation</param>
        /// <returns>The value if found, or null if it does not exist in the store</returns>
        Task<T?> GetAsync<T>(string key, CancellationToken cancellation);

        /// <summary>
        /// Asynchronously sets (or updates) a cached value in the backing cache store
        /// </summary>
        /// <typeparam name="T"></typeparam>
        /// <param name="key">The key identifying the object to recover from cache</param>
        /// <param name="newKey">An optional key that will be changed for the new object</param>
        /// <param name="cancellation">A token to cancel the async operation</param>
        /// <param name="value">The value to set at the given key</param>
        /// <returns>A task that completes when the update operation has compelted</returns>
        Task AddOrUpdateAsync<T>(string key, string? newKey, T value, CancellationToken cancellation);

        /// <summary>
        /// Asynchronously deletes an item from cache by its key
        /// </summary>
        /// <param name="key">The key identifying the item to delete</param>
        /// <param name="cancellation">A token to cancel the async operation</param>
        /// <returns>A task that completes when the delete operation has compelted</returns>
        Task<bool> DeleteAsync(string key, CancellationToken cancellation);

        /// <summary>
        /// Asynchronously gets a value from the backing cache store
        /// </summary>
        /// <typeparam name="T"></typeparam>
        /// <param name="key">The key identifying the object to recover from cache</param>
        /// <param name="deserializer">The specific deserialzer to deserialze the object</param>
        /// <param name="cancellation">A token to cancel the async operation</param>
        /// <returns>The value if found, or null if it does not exist in the store</returns>
        Task<T?> GetAsync<T>(string key, ICacheObjectDeserializer deserializer, CancellationToken cancellation);

        /// <summary>
        /// Asynchronously sets (or updates) a cached value in the backing cache store
        /// </summary>
        /// <typeparam name="T"></typeparam>
        /// <param name="key">The key identifying the object to recover from cache</param>
        /// <param name="newKey">An optional key that will be changed for the new object</param>
        /// <param name="cancellation">A token to cancel the async operation</param>
        /// <param name="value">The value to set at the given key</param>
        /// <param name="serialzer">The <see cref="ICacheObjectSerializer"/> used to serialze the entity</param>
        /// <returns>A task that completes when the update operation has compelted</returns>
        Task AddOrUpdateAsync<T>(string key, string? newKey, T value, ICacheObjectSerializer serialzer, CancellationToken cancellation);

        /// <summary>
        /// Asynchronously gets a value from the backing cache store and writes it to the 
        /// supplied data buffer
        /// </summary>
        /// <param name="key">The key identifying the object to recover from cache</param>
        /// <param name="callback">The callback method that will get the raw object data</param>
        /// <param name="state">The state parameter to pass to the callback when invoked</param>
        /// <param name="cancellation">A token to cancel the async operation</param>
        /// <returns>A task that complets when the object data has been written to the data buffer</returns>
        Task GetAsync<T>(string key, ObjectDataSet<T> callback, T state, CancellationToken cancellation);

        /// <summary>
        /// Asynchronously sets (or updates) a cached value in the backing cache store 
        /// from the supplied raw data
        /// </summary>
        /// <param name="key">The key identifying the object to recover from cache</param>
        /// <param name="newKey">An optional key that will be changed for the new object</param>
        /// <param name="callback">A callback method that will set the raw object data when received</param>
        /// <param name="state">The callback state parameter</param>
        /// <param name="cancellation">A token to cancel the async operation</param>
        /// <returns>A task that completes when the update operation has compelted</returns>
        Task AddOrUpdateAsync<T>(string key, string? newKey, ObjectDataReader<T> callback, T state, CancellationToken cancellation);
    }
}