/*
* Copyright (c) 2023 Vaughn Nugent
* 
* Library: VNLib
* Package: ObjectCacheServer
* File: CacheStore.cs 
*
* CacheStore.cs is part of ObjectCacheServer which is part of the larger 
* VNLib collection of libraries and utilities.
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
using System.Threading;
using System.Threading.Tasks;

using VNLib.Utils.Logging;
using VNLib.Plugins;
using VNLib.Plugins.Extensions.Loading;

namespace VNLib.Data.Caching.ObjectCache.Server.Cache
{
    /*
     * Implements the blob cache store, which is an abstraction around the blob cache listener.
     * This allows for publishing local events (say from other nodes) to keep caches in sync.
     */

    [ConfigurationName("cache")]
    internal sealed class CacheStore : ICacheStore, IDisposable
    {
        /// <summary>
        /// Gets the underlying cache listener
        /// </summary>
        public BlobCacheListener Listener { get; }


        public CacheStore(PluginBase plugin, IConfigScope config)
        {
            //Init cache
            Listener = InitializeCache((ObjectCacheServerEntry)plugin, config);
        }

        ///<inheritdoc/>
        ValueTask ICacheStore.AddOrUpdateBlobAsync<T>(string objectId, string? alternateId, GetBodyDataCallback<T> bodyData, T state, CancellationToken token)
        {
            return Listener.Cache.AddOrUpdateObjectAsync(objectId, alternateId, bodyData, state, default, token);
        }

        ///<inheritdoc/>
        void ICacheStore.Clear()
        {
            throw new NotImplementedException();
        }

        ///<inheritdoc/>
        ValueTask<bool> ICacheStore.DeleteItemAsync(string id, CancellationToken token)
        {
            return Listener.Cache.DeleteObjectAsync(id, token);
        }

        private static BlobCacheListener InitializeCache(ObjectCacheServerEntry plugin, IConfigScope config)
        {
            const string CacheConfigTemplate =
@"
Cache Configuration:
    Max memory: {max} Mb
    Buckets: {bc}
    Entries per-bucket: {mc}
";

            //Deserialize the cache config
            CacheConfiguration cacheConf = config.Deserialze<CacheConfiguration>();

            if (cacheConf.MaxCacheEntries < 2)
            {
                throw new ArgumentException("You must configure a 'max_cache' size larger than 1 item");
            }

            //Suggestion
            if (cacheConf.MaxCacheEntries < 200)
            {
                plugin.Log.Information("Suggestion: You may want a larger cache size, you have less than 200 items in cache");
            }

            //calculate the max memory usage
            ulong maxByteSize = cacheConf.MaxCacheEntries * (ulong)cacheConf.BucketCount * (ulong)cacheConf.MaxMessageSize;

            //Log the cache config
            plugin.Log.Information(CacheConfigTemplate,
                maxByteSize / (1024 * 1000),
                cacheConf.BucketCount,
                cacheConf.MaxCacheEntries
            );

            //Get the event listener
            ICacheListenerEventQueue queue = plugin.GetOrCreateSingleton<CacheListenerPubQueue>();

            //Get the memory manager
            ICacheMemoryManagerFactory manager = plugin.GetOrCreateSingleton<BucketLocalManagerFactory>();

            //Load the blob cache table system
            IBlobCacheTable bc = plugin.LoadMemoryCacheSystem(config, manager, cacheConf);

            //Endpoint only allows for a single reader
            return new(bc, queue, plugin.Log, plugin.ListenerHeap);
        }

        /*
         * Cleaned up by the plugin on exit
         */
        public void Dispose()
        {
            Listener.Dispose();
        }
    }
}
