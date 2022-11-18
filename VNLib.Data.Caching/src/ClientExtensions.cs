using System;
using System.IO;
using System.Linq;
using System.Buffers;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using System.Collections.Generic;
using System.Text.Json.Serialization;
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
        private static readonly JsonSerializerOptions LocalOptions = new()
        {
            DictionaryKeyPolicy = JsonNamingPolicy.CamelCase,
            NumberHandling = JsonNumberHandling.Strict,
            ReadCommentHandling = JsonCommentHandling.Disallow,
            WriteIndented = false,
            DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
            IgnoreReadOnlyFields = true,
            PropertyNameCaseInsensitive = true,
            IncludeFields = false,

            //Use small buffers
            DefaultBufferSize = 128
        };


        private static readonly ConditionalWeakTable<FBMClient, SemaphoreSlim> GetLock = new();
        private static readonly ConditionalWeakTable<FBMClient, SemaphoreSlim> UpdateLock = new();

        private static SemaphoreSlim GetLockCtor(FBMClient client) => new (50);

        private static SemaphoreSlim UpdateLockCtor(FBMClient client) => new (25);

        /// <summary>
        /// Gets an object from the server if it exists
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
        public static async Task<T?> GetObjectAsync<T>(this FBMClient client, string objectId, CancellationToken cancellationToken = default)
        {
            client.Config.DebugLog?.Debug("[DEBUG] Getting object {id}", objectId);
            SemaphoreSlim getLock = GetLock.GetValue(client, GetLockCtor);
            //Wait for entry
            await getLock.WaitAsync(cancellationToken);
            //Rent a new request
            FBMRequest request = client.RentRequest();
            try
            {
                //Set action as get/create
                request.WriteHeader(HeaderCommand.Action, Actions.Get);
                //Set session-id header
                request.WriteHeader(Constants.ObjectId, objectId);
                
                //Make request
                using FBMResponse response = await client.SendAsync(request, cancellationToken);

                response.ThrowIfNotSet();
                //Get the status code
                ReadOnlyMemory<char> status = response.Headers.FirstOrDefault(static a => a.Key == HeaderCommand.Status).Value;
                if (status.Span.Equals(ResponseCodes.Okay, StringComparison.Ordinal))
                {
                    return JsonSerializer.Deserialize<T>(response.ResponseBody, LocalOptions);
                }
                //Session may not exist on the server yet
                if (status.Span.Equals(ResponseCodes.NotFound, StringComparison.Ordinal))
                {
                    return default;
                }
                throw new InvalidStatusException("Invalid status code recived for object get request", status.ToString());
            }
            finally
            {
                getLock.Release();
                client.ReturnRequest(request);
            }
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
        public static async Task AddOrUpdateObjectAsync<T>(this FBMClient client, string objectId, string? newId, T data, CancellationToken cancellationToken = default)
        {
            client.Config.DebugLog?.Debug("[DEBUG] Updating object {id}, newid {nid}", objectId, newId);
            SemaphoreSlim updateLock = UpdateLock.GetValue(client, UpdateLockCtor);
            //Wait for entry
            await updateLock.WaitAsync(cancellationToken);
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
                //Get the body writer for the message
                IBufferWriter<byte> bodyWriter = request.GetBodyWriter();
                //Write json data to the message
                using (Utf8JsonWriter jsonWriter = new(bodyWriter))
                {
                    JsonSerializer.Serialize(jsonWriter, data, LocalOptions);
                }

                //Make request
                using FBMResponse response = await client.SendAsync(request, cancellationToken);

                response.ThrowIfNotSet();
                //Get the status code
                ReadOnlyMemory<char> status = response.Headers.FirstOrDefault(static a => a.Key == HeaderCommand.Status).Value;
                //Check status code
                if (status.Span.Equals(ResponseCodes.Okay, StringComparison.OrdinalIgnoreCase))
                {
                    return;
                }
                else if(status.Span.Equals(ResponseCodes.NotFound, StringComparison.OrdinalIgnoreCase))
                {
                    throw new ObjectNotFoundException($"object {objectId} not found on remote server");
                }
                //Invalid status
                throw new InvalidStatusException("Invalid status code recived for object upsert request", status.ToString());
            }
            finally
            {
                updateLock.Release();
                //Return the request(clears data and reset)
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
        public static async Task DeleteObjectAsync(this FBMClient client, string objectId, CancellationToken cancellationToken = default)
        {
            client.Config.DebugLog?.Debug("[DEBUG] Deleting object {id}", objectId);

            SemaphoreSlim updateLock = UpdateLock.GetValue(client, UpdateLockCtor);
            //Wait for entry
            await updateLock.WaitAsync(cancellationToken);
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
                ReadOnlyMemory<char> status = response.Headers.FirstOrDefault(static a => a.Key == HeaderCommand.Status).Value;
                if (status.Span.Equals(ResponseCodes.Okay, StringComparison.Ordinal))
                {
                    return;
                }
                else if(status.Span.Equals(ResponseCodes.NotFound, StringComparison.OrdinalIgnoreCase))
                {
                    throw new ObjectNotFoundException($"object {objectId} not found on remote server");
                }
                throw new InvalidStatusException("Invalid status code recived for object get request", status.ToString());
            }
            finally
            {
                updateLock.Release();
                client.ReturnRequest(request);
            }
        }

        /// <summary>
        /// Dequeues a change event from the server event queue for the current connection, or waits until a change happens
        /// </summary>
        /// <param name="client"></param>
        /// <param name="cancellationToken">A token to cancel the deuque operation</param>
        /// <returns>A <see cref="KeyValuePair{TKey, TValue}"/> that contains the modified object id and optionally its new id</returns>
        public static async Task<WaitForChangeResult> WaitForChangeAsync(this FBMClient client, CancellationToken cancellationToken = default)
        {
            //Rent a new request
            FBMRequest request = client.RentRequest();
            try
            {
                //Set action as event dequeue to dequeue a change event
                request.WriteHeader(HeaderCommand.Action, Actions.Dequeue);

                //Make request
                using FBMResponse response = await client.SendAsync(request, cancellationToken);

                response.ThrowIfNotSet();

                return new()
                {
                    Status = response.Headers.FirstOrDefault(static a => a.Key == HeaderCommand.Status).Value.ToString(),
                    CurrentId = response.Headers.SingleOrDefault(static v => v.Key == Constants.ObjectId).Value.ToString(),
                    NewId = response.Headers.SingleOrDefault(static v => v.Key == Constants.NewObjectId).Value.ToString()
                };
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
        public static string ObjectId(this FBMContext context)
        {
            return context.Request.Headers.First(static kvp => kvp.Key == Constants.ObjectId).Value.ToString();
        }
        /// <summary>
        /// Gets the new ID of the object if specified from the request. Null if the request did not specify an id update
        /// </summary>
        /// <param name="context"></param>
        /// <returns>The new ID of the object if speicifed, null otherwise</returns>
        public static string? NewObjectId(this FBMContext context)
        {
            return context.Request.Headers.FirstOrDefault(static kvp => kvp.Key == Constants.NewObjectId).Value.ToString();
        }
        /// <summary>
        /// Gets the request method for the request
        /// </summary>
        /// <param name="context"></param>
        /// <returns>The request method string</returns>
        public static string Method(this FBMContext context)
        {
            return context.Request.Headers.First(static kvp => kvp.Key == HeaderCommand.Action).Value.ToString();
        }
        /// <summary>
        /// Closes a response with a status code
        /// </summary>
        /// <param name="context"></param>
        /// <param name="responseCode">The status code to send to the client</param>
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
