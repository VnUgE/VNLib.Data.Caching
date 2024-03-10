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

using VNLib.Utils.Resources;
using VNLib.Plugins;
using VNLib.Plugins.Extensions.Loading;
using VNLib.Utils.Extensions;

namespace VNLib.Data.Caching.ObjectCache.Server.Cache
{
    internal static class CacheSystemUtil
    {
        const string PERSISTANT_ASM_CONFIF_KEY = "persistant_cache_asm";
        const string USER_CACHE_ASM_CONFIG_KEY = "custom_cache_impl_asm";
        const string INITIALIZE_METHOD_NAME = "OnInitializeForBucket";
        const string LOAD_METHOD_NAME = "OnRuntimeLoad";        

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
        public static IBlobCacheTable LoadMemoryCacheSystem(this PluginBase plugin, IConfigScope config, ICacheMemoryManagerFactory heap, CacheMemoryConfiguration cacheConf)
        {
#pragma warning disable CA2000 // Dispose objects before losing scope

            //First, try to load persitant cache store
            IPersistantCacheStore? pCManager = GetPersistantStore(plugin, config);

#pragma warning restore CA2000 // Dispose objects before losing scope

            IBlobCacheTable table;

            //See if the user defined a custom cache table implementation
            if (config.TryGetValue(USER_CACHE_ASM_CONFIG_KEY, out JsonElement customEl))
            {
                string asmName = customEl.GetString() ?? throw new FileNotFoundException("User defined a custom blob cache assembly but the file name was null");

                //Return the runtime loaded table
                table = plugin.CreateServiceExternal<IBlobCacheTable>(asmName);

                //Try to get the load method and pass the persistant cache instance
                ManagedLibrary.TryGetMethod<Action<PluginBase, IPersistantCacheStore?>>(table, LOAD_METHOD_NAME)?.Invoke(plugin, pCManager);
            }
            else
            {
                //Default type
                table = new BlobCacheTable(cacheConf.BucketCount, cacheConf.MaxCacheEntries, heap, pCManager);
            }

            if(pCManager != null)
            {
                //Initialize the subsystem from the cache table
                InitializeSubsystem(pCManager, table);
            }

            return table;
        }

        private static void InitializeSubsystem(IPersistantCacheStore store, IBlobCacheTable table)
        {
            //Try to get the Initialize method
            Action<uint>? initMethod = ManagedLibrary.TryGetMethod<Action<uint>>(store, INITIALIZE_METHOD_NAME);

            if(initMethod != null)
            {
                //Itterate all buckets
                table.ForEach(bucket => initMethod(bucket.Id));
            }
        }

        private static IPersistantCacheStore? GetPersistantStore(PluginBase plugin, IConfigScope config)
        {
            //Get the persistant assembly 
            if (!config.TryGetValue(PERSISTANT_ASM_CONFIF_KEY, out JsonElement asmEl))
            {
                return null;
            }

            string? asmName = asmEl.GetString();
            if (string.IsNullOrWhiteSpace(asmName))
            {
                return null;
            }

            //Return the 
            return plugin.CreateServiceExternal<IPersistantCacheStore>(asmName);
        }
        
    }
}
