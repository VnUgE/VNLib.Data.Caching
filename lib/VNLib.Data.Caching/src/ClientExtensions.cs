/*
* Copyright (c) 2023 Vaughn Nugent
* 
* Library: VNLib
* Package: VNLib.Data.Caching
* File: ClientExtensions.cs 
*
* ClientExtensions.cs is part of VNLib.Data.Caching which is part of the larger 
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
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using System.Runtime.CompilerServices;

using VNLib.Utils.Logging;
using VNLib.Net.Messaging.FBM;
using VNLib.Net.Messaging.FBM.Client;
using VNLib.Net.Messaging.FBM.Server;
using VNLib.Data.Caching.Exceptions;
using static VNLib.Data.Caching.Constants;

namespace VNLib.Data.Caching
{

    /// <summary>
    /// Provides caching extension methods for <see cref="FBMClient"/>
    /// </summary>
    public static class ClientExtensions
    {

        private static readonly JsonCacheObjectSerializer DefaultSerializer = new();

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static void LogDebug(this FBMClient client, string message, params object?[] args)
        {
            client.Config.DebugLog?.Debug($"[CACHE] : {message}", args);
        }     

        /// <summary>
        /// Updates the state of the object, and optionally updates the ID of the object. The data 
        /// parameter is serialized, buffered, and streamed to the remote server
        /// </summary>
        /// <typeparam name="T"></typeparam>
        /// <param name="client"></param>
        /// <param name="objectId">The id of the object to update or replace</param>
        /// <param name="newId">An optional parameter to specify a new ID for the old object</param>
        /// <param name="data">The payload data to serialize and set as the data state of the session</param>
        /// <param name="cancellationToken">A token to cancel the operation</param>
        /// <returns>A task that resolves when the server responds</returns>
        /// <exception cref="JsonException"></exception>
        /// <exception cref="OutOfMemoryException"></exception>
        /// <exception cref="InvalidStatusException"></exception>
        /// <exception cref="ObjectDisposedException"></exception>
        /// <exception cref="InvalidResponseException"></exception>
        /// <exception cref="MessageTooLargeException"></exception>
        /// <exception cref="ObjectNotFoundException"></exception>
        public static Task AddOrUpdateObjectAsync<T>(this FBMClient client, string objectId, string? newId, T data, CancellationToken cancellationToken = default)
        {
            //Use the default/json serialzer if not specified
            return AddOrUpdateObjectAsync(client, objectId, newId, data, DefaultSerializer, cancellationToken);
        }      

        /// <summary>
        /// Updates the state of the object, and optionally updates the ID of the object. The data 
        /// parameter is serialized, buffered, and streamed to the remote server
        /// </summary>
        /// <typeparam name="T"></typeparam>
        /// <param name="client"></param>
        /// <param name="objectId">The id of the object to update or replace</param>
        /// <param name="newId">An optional parameter to specify a new ID for the old object</param>
        /// <param name="data">The payload data to serialize and set as the data state of the session</param>
        /// <param name="serializer">The custom serializer to used to serialze the object to binary</param>
        /// <param name="cancellationToken">A token to cancel the operation</param>
        /// <returns>A task that resolves when the server responds</returns>
        /// <exception cref="OutOfMemoryException"></exception>
        /// <exception cref="InvalidStatusException"></exception>
        /// <exception cref="ObjectDisposedException"></exception>
        /// <exception cref="InvalidResponseException"></exception>
        /// <exception cref="MessageTooLargeException"></exception>
        /// <exception cref="ObjectNotFoundException"></exception>
        public static async Task AddOrUpdateObjectAsync<T>(
            this FBMClient client,
            string objectId,
            string? newId,
            T data,
            ICacheObjectSerializer serializer,
            CancellationToken cancellationToken = default)
        {
            _ = client ?? throw new ArgumentNullException(nameof(client));
            _ = serializer ?? throw new ArgumentNullException(nameof(serializer));

            client.LogDebug("Updating object {id}, newid {nid}", objectId, newId);

            //Rent a new request
            FBMRequest request = client.RentRequest();
            try
            {
                //Set action as get/create
                request.WriteHeader(HeaderCommand.Action, Actions.AddOrUpdate);

                //Set object-id header
                request.WriteHeader(Constants.ObjectId, objectId);

                //if new-id set, set the new-id header
                if (!string.IsNullOrWhiteSpace(newId))
                {
                    request.WriteHeader(Constants.NewObjectId, newId);
                }

                //Serialize the message using the request buffer
                serializer.Serialize(data, request.GetBodyWriter());

                //Make request
                using FBMResponse response = await client.SendAsync(request, cancellationToken);
                response.ThrowIfNotSet();

                //Get the status code
                FBMMessageHeader status = response.Headers.FirstOrDefault(static a => a.Header == HeaderCommand.Status);

                //Check status code
                if (status.Value.Equals(ResponseCodes.Okay, StringComparison.OrdinalIgnoreCase))
                {
                    return;
                }
                else if (status.Value.Equals(ResponseCodes.NotFound, StringComparison.OrdinalIgnoreCase))
                {
                    throw new ObjectNotFoundException($"object {objectId} not found on remote server");
                }

                //Invalid status
                throw new InvalidStatusException("Invalid status code recived for object upsert request", status.ToString());
            }
            finally
            {
                //Return the request(clears data and reset)
                client.ReturnRequest(request);
            }
        }

        /// <summary>
        /// Updates the state of the object, and optionally updates the ID of the object. The data 
        /// parameter is serialized, buffered, and streamed to the remote server
        /// </summary>
        /// <param name="client"></param>
        /// <param name="objectId">The id of the object to update or replace</param>
        /// <param name="newId">An optional parameter to specify a new ID for the old object</param>
        /// <param name="data">An <see cref="IObjectData"/> that represents the data to set</param>
        /// <param name="cancellationToken">A token to cancel the operation</param>
        /// <returns>A task that resolves when the server responds</returns>
        /// <exception cref="OutOfMemoryException"></exception>
        /// <exception cref="InvalidStatusException"></exception>
        /// <exception cref="ObjectDisposedException"></exception>
        /// <exception cref="InvalidResponseException"></exception>
        /// <exception cref="MessageTooLargeException"></exception>
        /// <exception cref="ObjectNotFoundException"></exception>
        public static Task AddOrUpdateObjectAsync(this FBMClient client, string objectId, string? newId, IObjectData data, CancellationToken cancellationToken = default)
        {
            return AddOrUpdateObjectAsync(client, objectId, newId, static d => d.GetData(), data, cancellationToken);
        }

        /// <summary>
        /// Updates the state of the object, and optionally updates the ID of the object. The data 
        /// parameter is serialized, buffered, and streamed to the remote server
        /// </summary>
        /// <param name="client"></param>
        /// <param name="objectId">The id of the object to update or replace</param>
        /// <param name="newId">An optional parameter to specify a new ID for the old object</param>
        /// <param name="callback">A callback method that will return the desired object data</param>
        /// <param name="cancellationToken">A token to cancel the operation</param>
        /// <param name="state">The state to be passed to the callback</param>
        /// <returns>A task that resolves when the server responds</returns>
        /// <exception cref="OutOfMemoryException"></exception>
        /// <exception cref="InvalidStatusException"></exception>
        /// <exception cref="ObjectDisposedException"></exception>
        /// <exception cref="InvalidResponseException"></exception>
        /// <exception cref="MessageTooLargeException"></exception>
        /// <exception cref="ObjectNotFoundException"></exception>
        public async static Task AddOrUpdateObjectAsync<T>(this FBMClient client, string objectId, string? newId, ObjectDataReader<T> callback, T state, CancellationToken cancellationToken = default)
        {
            _ = client ?? throw new ArgumentNullException(nameof(client));
            _ = callback ?? throw new ArgumentNullException(nameof(callback));

            client.LogDebug("Updating object {id}, newid {nid}", objectId, newId);

            //Rent a new request
            FBMRequest request = client.RentRequest();
            try
            {
                //Set action as get/create
                request.WriteHeader(HeaderCommand.Action, Actions.AddOrUpdate);

                //Set session-id header
                request.WriteHeader(Constants.ObjectId, objectId);

                //if new-id set, set the new-id header
                if (!string.IsNullOrWhiteSpace(newId))
                {
                    request.WriteHeader(Constants.NewObjectId, newId);
                }

                //Write the message body as the objet data
                request.WriteBody(callback(state));

                //Make request
                using FBMResponse response = await client.SendAsync(request, cancellationToken);
                response.ThrowIfNotSet();

                //Get the status code
                FBMMessageHeader status = response.Headers.FirstOrDefault(static a => a.Header == HeaderCommand.Status);

                //Check status code
                if (status.Value.Equals(ResponseCodes.Okay, StringComparison.OrdinalIgnoreCase))
                {
                    return;
                }
                else if (status.Value.Equals(ResponseCodes.NotFound, StringComparison.OrdinalIgnoreCase))
                {
                    throw new ObjectNotFoundException($"object {objectId} not found on remote server");
                }

                //Invalid status
                throw new InvalidStatusException("Invalid status code recived for object upsert request", status.ToString());
            }
            finally
            {
                //Return the request(clears data and reset)
                client.ReturnRequest(request);
            }
        }

        /// <summary>
        /// Gets an object from the server if it exists, and uses the default serialzer to 
        /// recover the object
        /// </summary>
        /// <typeparam name="T"></typeparam>
        /// <param name="client"></param>
        /// <param name="objectId">The id of the object to get</param>
        /// <param name="cancellationToken">A token to cancel the operation</param>
        /// <returns>A task that completes to return the results of the response payload</returns>
        /// <exception cref="JsonException"></exception>
        /// <exception cref="OutOfMemoryException"></exception>
        /// <exception cref="InvalidStatusException"></exception>
        /// <exception cref="ObjectDisposedException"></exception>
        /// <exception cref="InvalidResponseException"></exception>
        public static Task<T?> GetObjectAsync<T>(this FBMClient client, string objectId, CancellationToken cancellationToken = default)
        {
            return GetObjectAsync<T>(client, objectId, DefaultSerializer, cancellationToken);
        }

        /// <summary>
        /// Gets an object from the server if it exists
        /// </summary>
        /// <typeparam name="T"></typeparam>
        /// <param name="client"></param>
        /// <param name="objectId">The id of the object to get</param>
        /// <param name="cancellationToken">A token to cancel the operation</param>
        /// <param name="deserialzer">The custom data deserialzer used to deserialze the binary cache result</param>
        /// <returns>A task that completes to return the results of the response payload</returns>
        /// <exception cref="InvalidStatusException"></exception>
        /// <exception cref="ObjectDisposedException"></exception>
        /// <exception cref="InvalidResponseException"></exception>
        public static async Task<T?> GetObjectAsync<T>(this FBMClient client, string objectId, ICacheObjectDeserializer deserialzer, CancellationToken cancellationToken = default)
        {
            _ = client ?? throw new ArgumentNullException(nameof(client));
            _ = deserialzer ?? throw new ArgumentNullException(nameof(deserialzer));

            client.LogDebug("Getting object {id}", objectId);

            //Rent a new request
            FBMRequest request = client.RentRequest();
            try
            {
                //Set action as get/create
                request.WriteHeader(HeaderCommand.Action, Actions.Get);

                //Set object id header
                request.WriteHeader(Constants.ObjectId, objectId);

                //Make request
                using FBMResponse response = await client.SendAsync(request, cancellationToken);
                response.ThrowIfNotSet();

                //Get the status code
                FBMMessageHeader status = response.Headers.FirstOrDefault(static a => a.Header == HeaderCommand.Status);

                //Check ok status code, then its safe to deserialize
                if (status.Value.Equals(ResponseCodes.Okay, StringComparison.Ordinal))
                {
                    return deserialzer.Deserialize<T>(response.ResponseBody);
                }

                //Object  may not exist on the server yet
                if (status.Value.Equals(ResponseCodes.NotFound, StringComparison.Ordinal))
                {
                    return default;
                }

                throw new InvalidStatusException("Invalid status code recived for object get request", status.ToString());
            }
            finally
            {
                client.ReturnRequest(request);
            }
        }

        /// <summary>
        /// Gets an object from the server if it exists. If data is retreived, it sets
        /// the <see cref="IObjectData.SetData(ReadOnlySpan{byte})"/>, if no data is 
        /// found, this method returns and never calls SetData.
        /// </summary>
        /// <param name="client"></param>
        /// <param name="objectId">The id of the object to get</param>
        /// <param name="data">An object data instance used to store the found object data</param>
        /// <param name="cancellationToken">A token to cancel the operation</param>
        /// <returns>A task that completes to return the results of the response payload</returns>
        /// <exception cref="InvalidStatusException"></exception>
        /// <exception cref="ObjectDisposedException"></exception>
        /// <exception cref="InvalidResponseException"></exception>
        public static Task<bool> GetObjectAsync(this FBMClient client, string objectId, IObjectData data, CancellationToken cancellationToken = default)
        {
            return GetObjectAsync(client, objectId, static (p, d) => p.SetData(d), data, cancellationToken);
        }

        /// <summary>
        /// Gets an object from the server if it exists. If data is retreived, it sets
        /// the <see cref="IObjectData.SetData(ReadOnlySpan{byte})"/>, if no data is 
        /// found, this method returns and never calls SetData.
        /// </summary>
        /// <param name="client"></param>
        /// <param name="objectId">The id of the object to get</param>
        /// <param name="setter">A callback method used to store the recovered object data</param>
        /// <param name="state">The state parameter to pass to the callback method</param>
        /// <param name="cancellationToken">A token to cancel the operation</param>
        /// <returns>When complete, true if the object was found, false if not found, and an exception otherwise</returns>
        /// <exception cref="InvalidStatusException"></exception>
        /// <exception cref="ObjectDisposedException"></exception>
        /// <exception cref="InvalidResponseException"></exception>
        public static async Task<bool> GetObjectAsync<T>(this FBMClient client, string objectId, ObjectDataSet<T> setter, T state, CancellationToken cancellationToken = default)
        {
            _ = client ?? throw new ArgumentNullException(nameof(client));
            _ = setter ?? throw new ArgumentNullException(nameof(setter));

            client.LogDebug("Getting object {id}", objectId);

            //Rent a new request
            FBMRequest request = client.RentRequest();
            try
            {
                //Set action as get/create
                request.WriteHeader(HeaderCommand.Action, Actions.Get);

                //Set object id header
                request.WriteHeader(Constants.ObjectId, objectId);

                //Make request
                using FBMResponse response = await client.SendAsync(request, cancellationToken);
                response.ThrowIfNotSet();

                //Get the status code
                FBMMessageHeader status = response.Headers.FirstOrDefault(static a => a.Header == HeaderCommand.Status);

                //Check ok status code, then its safe to deserialize
                if (status.Value.Equals(ResponseCodes.Okay, StringComparison.Ordinal))
                {
                    //Write the object data
                    setter(state, response.ResponseBody);
                    return true;
                }

                //Object may not exist on the server yet
                if (status.Value.Equals(ResponseCodes.NotFound, StringComparison.Ordinal))
                {
                    return false;
                }

                throw new InvalidStatusException("Invalid status code recived for object get request", status.ToString());
            }
            finally
            {
                client.ReturnRequest(request);
            }
        }

        /// <summary>
        /// Asynchronously deletes an object in the remote store
        /// </summary>
        /// <param name="client"></param>
        /// <param name="objectId">The id of the object to update or replace</param>
        /// <param name="cancellationToken">A token to cancel the operation</param>
        /// <returns>A task that resolves when the operation has completed</returns>
        /// <exception cref="InvalidStatusException"></exception>
        /// <exception cref="ObjectDisposedException"></exception>
        /// <exception cref="InvalidResponseException"></exception>
        /// <exception cref="ObjectNotFoundException"></exception>
        public static async Task<bool> DeleteObjectAsync(this FBMClient client, string objectId, CancellationToken cancellationToken = default)
        {
            _ = client ?? throw new ArgumentNullException(nameof(client));

            client.LogDebug("Deleting object {id}", objectId);

            //Rent a new request
            FBMRequest request = client.RentRequest();
            try
            {
                //Set action as delete
                request.WriteHeader(HeaderCommand.Action, Actions.Delete);
                //Set session-id header
                request.WriteHeader(Constants.ObjectId, objectId);

                //Make request
                using FBMResponse response = await client.SendAsync(request, cancellationToken);
                response.ThrowIfNotSet();

                //Get the status code
                FBMMessageHeader status = response.Headers.FirstOrDefault(static a => a.Header == HeaderCommand.Status);

                if (status.Value.Equals(ResponseCodes.Okay, StringComparison.Ordinal))
                {
                    return true;
                }
                else if (status.Value.Equals(ResponseCodes.NotFound, StringComparison.OrdinalIgnoreCase))
                {
                    return false;
                }

                throw new InvalidStatusException("Invalid status code recived for object get request", status.ToString());
            }
            finally
            {
                client.ReturnRequest(request);
            }
        }

        /// <summary>
        /// Dequeues a change event from the server event queue for the current connection, or waits until a change happens
        /// </summary>
        /// <param name="client"></param>
        /// <param name="change">The instance to store change event data to</param>
        /// <param name="cancellationToken">A token to cancel the deuque operation</param>
        /// <returns>A <see cref="WaitForChangeResult"/> that contains information about the modified element</returns>
        /// <exception cref="InvalidResponseException"></exception>
        /// <exception cref="InvalidOperationException"></exception>
        public static async Task WaitForChangeAsync(this FBMClient client, WaitForChangeResult change, CancellationToken cancellationToken = default)
        {
            _ = change ?? throw new ArgumentNullException(nameof(change));

            //Rent a new request
            FBMRequest request = client.RentRequest();
            try
            {
                //Set action as event dequeue to dequeue a change event
                request.WriteHeader(HeaderCommand.Action, Actions.Dequeue);

                //Make request
                using FBMResponse response = await client.SendAsync(request, cancellationToken);

                response.ThrowIfNotSet();

                change.Status = response.Headers.FirstOrDefault(static a => a.Header == HeaderCommand.Status).Value.ToString();
                change.CurrentId = response.Headers.SingleOrDefault(static v => v.Header == Constants.ObjectId).Value.ToString();
                change.NewId = response.Headers.SingleOrDefault(static v => v.Header == Constants.NewObjectId).Value.ToString();
            }
            finally
            {
                client.ReturnRequest(request);
            }
        }

        /// <summary>
        /// Gets the Object-id for the request message, or throws an <see cref="InvalidOperationException"/> if not specified
        /// </summary>
        /// <param name="context"></param>
        /// <returns>The id of the object requested</returns>
        /// <exception cref="InvalidOperationException"></exception>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static string ObjectId(this FBMContext context)
        {
            return context.Request.Headers.First(static kvp => kvp.Header == Constants.ObjectId).Value.ToString();
        }
        
        /// <summary>
        /// Gets the new ID of the object if specified from the request. Null if the request did not specify an id update
        /// </summary>
        /// <param name="context"></param>
        /// <returns>The new ID of the object if speicifed, null otherwise</returns>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static string? NewObjectId(this FBMContext context)
        {
            return context.Request.Headers.FirstOrDefault(static kvp => kvp.Header == Constants.NewObjectId).GetValueString();
        }

        /// <summary>
        /// Gets the request method for the request
        /// </summary>
        /// <param name="context"></param>
        /// <returns>The request method string</returns>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static string Method(this FBMContext context)
        {
            return context.Request.Headers.First(static kvp => kvp.Header == HeaderCommand.Action).Value.ToString();
        }

        /// <summary>
        /// Closes a response with a status code
        /// </summary>
        /// <param name="context"></param>
        /// <param name="responseCode">The status code to send to the client</param>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static void CloseResponse(this FBMContext context, string responseCode)
        {
            context.Response.WriteHeader(HeaderCommand.Status, responseCode);
        }

        /// <summary>
        /// Initializes the worker for a reconnect policy and returns an object that can listen for changes
        /// and configure the connection as necessary
        /// </summary>
        /// <param name="worker"></param>
        /// <param name="retryDelay">The amount of time to wait between retries</param>
        /// <param name="serverUri">The uri to reconnect the client to</param>
        /// <returns>A <see cref="ClientRetryManager{T}"/> for listening for retry events</returns>
        public static ClientRetryManager<T> SetReconnectPolicy<T>(this T worker, TimeSpan retryDelay, Uri serverUri) where T: IStatefulConnection
        {
            //Return new manager
            return new (worker, retryDelay, serverUri);
        }
       
    }
}
