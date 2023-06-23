/*
* Copyright (c) 2023 Vaughn Nugent
* 
* Library: VNLib
* Package: ObjectCacheServer
* File: CacheSystemUtil.cs 
*
* CacheSystemUtil.cs is part of ObjectCacheServer which is part of the larger 
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
using System.IO;
using System.Text.Json;
using System.Collections;
using System.Collections.Generic;
using System.Runtime.CompilerServices;

using VNLib.Plugins;
using VNLib.Utils.Memory;
using VNLib.Plugins.Extensions.Loading;

namespace VNLib.Data.Caching.ObjectCache.Server
{
    internal static class CacheSystemUtil
    {
        const string PERSISTANT_ASM_CONFIF_KEY = "persistant_cache_asm";
        const string USER_CACHE_ASM_CONFIG_KEY = "custom_cache_impl_asm";
        const string LOAD_METHOD_NAME = "OnRuntimeLoad";
        const string TEARDOWN_METHOD_NAME = "OnSystemDetach";

        /// <summary>
        /// Loads the <see cref="IBlobCacheTable"/> implementation (dynamic or default) into the process
        /// and initializes it and it's backing store.
        /// </summary>
        /// <param name="plugin"></param>
        /// <param name="config">The configuration object that contains loading variables</param>
        /// <param name="heap">The heap for memory cache table to allocate buffers from</param>
        /// <param name="cacheConf">The cache configuration object</param>
        /// <returns>The loaded <see cref="IBlobCacheTable"/> implementation</returns>
        /// <exception cref="FileNotFoundException"></exception>
        public static IBlobCacheTable LoadMemoryCacheSystem(this PluginBase plugin, IConfigScope config, IUnmangedHeap heap, CacheConfiguration cacheConf)
        {
            //First, try to load persitant cache store
            PersistantCacheManager? pCManager = GetPersistantStore(plugin, config);

            IBlobCacheTable table;

            //See if the user defined a custom cache table implementation
            if (config.TryGetValue(USER_CACHE_ASM_CONFIG_KEY, out JsonElement customEl))
            {
                string asmName = customEl.GetString() ?? throw new FileNotFoundException("User defined a custom blob cache assembly but the file name was null");

                //Return the runtime loaded table
                table = LoadCustomMemCacheTable(plugin, asmName, pCManager);
            }
            else
            {
                //Default type
                table =  GetInternalBlobCache(heap, cacheConf, pCManager);
            }

            //Initialize the subsystem from the cache table
            pCManager?.InitializeSubsystem(table);

            return table;
        }

        private static IBlobCacheTable GetInternalBlobCache(IUnmangedHeap heap, CacheConfiguration config, IPersistantCacheStore? store)
        {
            return new BlobCacheTable(config.BucketCount, config.MaxCacheEntries, heap, store);
        }

        private static IBlobCacheTable LoadCustomMemCacheTable(PluginBase plugin, string asmName, IPersistantCacheStore? store)
        {
            //Load the custom assembly
            AssemblyLoader<IBlobCacheTable> customTable = plugin.LoadAssembly<IBlobCacheTable>(asmName);

            try
            {
                //Try get onload method and pass the persistant cache instance
                Action<PluginBase, IPersistantCacheStore?>? onLoad = customTable.TryGetMethod<Action<PluginBase, IPersistantCacheStore?>>(LOAD_METHOD_NAME);
                onLoad?.Invoke(plugin, store);
            }
            catch
            {
                customTable.Dispose();
                throw;
            }

            return new RuntimeBlobCacheTable(customTable);
        }

        private static PersistantCacheManager? GetPersistantStore(PluginBase plugin, IConfigScope config)
        {
            //Get the persistant assembly 
            if (!config.TryGetValue(PERSISTANT_ASM_CONFIF_KEY, out JsonElement asmEl))
            {
                return null;
            }

            string? asmName = asmEl.GetString();
            if (asmName == null)
            {
                return null;
            }

            //Load the dynamic assembly into the alc
            AssemblyLoader<IPersistantCacheStore> loader = plugin.LoadAssembly<IPersistantCacheStore>(asmName);
            try
            {
                //Call the OnLoad method
                Action<PluginBase, IConfigScope>? loadMethod = loader.TryGetMethod<Action<PluginBase, IConfigScope>>(LOAD_METHOD_NAME);

                loadMethod?.Invoke(plugin, config);
            }
            catch
            {
                loader.Dispose();
                throw;
            }

            //Return the 
            return new(loader);
        }
      

        private sealed class RuntimeBlobCacheTable : IBlobCacheTable 
        {

            private readonly IBlobCacheTable _table;
            private readonly Action? OnDetatch;

            public RuntimeBlobCacheTable(AssemblyLoader<IBlobCacheTable> loader)
            {
                OnDetatch = loader.TryGetMethod<Action>(TEARDOWN_METHOD_NAME);
                _table = loader.Resource;
            }

            public void Dispose()
            {
                //We can let the loader dispose the cache table, but we can notify of detatch
                OnDetatch?.Invoke();
            }


            ///<inheritdoc/>
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            IBlobCacheBucket IBlobCacheTable.GetBucket(ReadOnlySpan<char> objectId) => _table.GetBucket(objectId);

            ///<inheritdoc/>
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            public IEnumerator<IBlobCacheBucket> GetEnumerator() => _table.GetEnumerator();

            ///<inheritdoc/>
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            IEnumerator IEnumerable.GetEnumerator() => ((IEnumerable)_table).GetEnumerator();
        }

        internal sealed class PersistantCacheManager : IPersistantCacheStore
        {
            const string INITIALIZE_METHOD_NAME = "OnInitializeForBucket";
           

            /*
             * Our referrence can be technically unloaded, but so will 
             * this instance, since its loaded into the current ALC, so 
             * this referrence may exist for the lifetime of this instance.
             * 
             * It also implements IDisposable, which the assembly loader class
             * will call when this plugin is unloaded, we dont need to call
             * it here, but we can signal a detach.
             * 
             * Since the store implements IDisposable, its likely going to 
             * check for dispose on each call, so we don't need to add
             * and additional disposed check since the method calls must be fast.
             */

            private readonly IPersistantCacheStore store;

            private readonly Action<uint>? InitMethod;
            private readonly Action? OnServiceDetatch;

            public PersistantCacheManager(AssemblyLoader<IPersistantCacheStore> loader)
            {
                //Try to get the Initialize method
                InitMethod = loader.TryGetMethod<Action<uint>>(INITIALIZE_METHOD_NAME);

                //Get the optional detatch method
                OnServiceDetatch = loader.TryGetMethod<Action>(TEARDOWN_METHOD_NAME);

                store = loader.Resource;
            }

            /// <summary>
            /// Optionally initializes the backing store by publishing the table's bucket 
            /// id's so it's made aware of the memory cache bucket system.
            /// </summary>
            /// <param name="table">The table containing buckets to publish</param>
            public void InitializeSubsystem(IBlobCacheTable table)
            {
                //Itterate all buckets
                foreach (IBlobCacheBucket bucket in table)
                {
                    InitMethod?.Invoke(bucket.Id);
                }
            }

            void IDisposable.Dispose()
            {
                //Assembly loader will dispose the type, we can just signal a detach

                OnServiceDetatch?.Invoke();
            }

            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            bool IPersistantCacheStore.OnCacheMiss(uint bucketId, string key, IMemoryCacheEntryFactory factory, out CacheEntry entry)
            {
                return store.OnCacheMiss(bucketId, key, factory, out entry);
            }

            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            void IPersistantCacheStore.OnEntryDeleted(uint bucketId, string key) => store.OnEntryDeleted(bucketId, key);

            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            void IPersistantCacheStore.OnEntryEvicted(uint bucketId, string key, in CacheEntry entry) => store.OnEntryEvicted(bucketId, key, in entry);
        }
    }
}
