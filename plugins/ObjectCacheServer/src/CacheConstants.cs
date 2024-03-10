/*
* Copyright (c) 2024 Vaughn Nugent
* 
* Library: VNLib
* Package: ObjectCacheServer
* File: CacheConstants.cs 
*
* CacheConstants.cs is part of ObjectCacheServer which is 
* part of the larger VNLib collection of libraries and utilities.
*
* ObjectCacheServer is free software: you can redistribute it and/or modify 
* it under the terms of the GNU Affero General Public License as 
* published by the Free Software Foundation, either version 3 of the
* License, or (at your option) any later version.
*
* ObjectCacheServer is distributed in the hope that it will be useful,
* but WITHOUT ANY WARRANTY; without even the implied warranty of
* MERCHANTABILITY or FITNESS FOR A PARTICULAR PURPOSE. See the
* GNU Affero General Public License for more details.
*
* You should have received a copy of the GNU Affero General Public License
* along with this program.  If not, see https://www.gnu.org/licenses/.
*/

using System;

namespace VNLib.Data.Caching.ObjectCache.Server
{
    internal static class CacheConstants
    {
        /// <summary>
        /// The default path for the VNCache well known endpoint (aka discovery endpoint)
        /// </summary>
        public const string DefaultWellKnownPath = "/.well-known/vncache";

        /// <summary>
        /// The maximum size of buffers for FBM messages sent between servers.
        /// </summary>
        public const int MaxSyncMessageSize = 12 * 1024;

        /// <summary>
        /// The maximum size of the change queue for the cache listener
        /// </summary>
        public const int CacheListenerChangeQueueSize = 10000;

        /// <summary>
        /// The time a client authorization token is valid for
        /// </summary>
        public static readonly TimeSpan ClientAuthTokenExpiration = TimeSpan.FromSeconds(30);

        public static class LogScopes
        {
            /// <summary>
            /// The log scope for the cache listener
            /// </summary>
            public const string BlobCacheListener = "CacheListener";

            /// <summary>
            /// The peer discovery log scope
            /// </summary>
            public const string PeerDiscovery = "DISC";

            /// <summary>
            /// The log scope for the replication FBM client debug log (if debugging is enabled)
            /// </summary>
            public const string ReplicationFbmDebug = "REPL-CLNT";

            /// <summary>
            /// The log scope for cache replication events
            /// </summary>
            public const string RepliactionManager = "REPL-MGR";

            /// <summary>
            /// The log scope for the cache listener change event queue
            /// </summary>
            public const string CacheListenerPubQueue = "QUEUE";

            /// <summary>
            /// The log scope for the cache connection websocket endpoint
            /// </summary>
            public const string ConnectionEndpoint = "CONEP";
        }

        public static class Delays
        {
            /// <summary>
            /// The amount of startup delay before starting an initial peer discovery
            /// </summary>
            public static readonly TimeSpan InitialDiscovery = TimeSpan.FromSeconds(15);

            /// <summary>
            /// The amount of time to wait before retrying a failed resolve 
            /// of a well-known peers
            /// </summary>
            public static readonly TimeSpan WellKnownResolveFailed = TimeSpan.FromSeconds(20);

            /// <summary>
            /// The amount of time to wait when getting the value of a changed item from the cache
            /// </summary>
            /// <remarks>
            /// When an item change was detected from another peer, the cache will wait this 
            /// amount of time to get the new value from the cache before timing out.
            /// </remarks>
            public static readonly TimeSpan CacheSyncGetItemTimeout = TimeSpan.FromSeconds(10);
        }
    }
}
