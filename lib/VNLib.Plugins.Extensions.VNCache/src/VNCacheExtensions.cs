/*
* Copyright (c) 2024 Vaughn Nugent
* 
* Library: VNLib
* Package: VNLib.Plugins.Extensions.VNCache
* File: VNCacheExtensions.cs 
*
* VNCacheExtensions.cs is part of VNLib.Plugins.Extensions.VNCache which is part of the larger 
* VNLib collection of libraries and utilities.
*
* VNLib.Plugins.Extensions.VNCache is free software: you can redistribute it and/or modify 
* it under the terms of the GNU Affero General Public License as 
* published by the Free Software Foundation, either version 3 of the
* License, or (at your option) any later version.
*
* VNLib.Plugins.Extensions.VNCache is distributed in the hope that it will be useful,
* but WITHOUT ANY WARRANTY; without even the implied warranty of
* MERCHANTABILITY or FITNESS FOR A PARTICULAR PURPOSE. See the
* GNU Affero General Public License for more details.
*
* You should have received a copy of the GNU Affero General Public License
* along with this program.  If not, see https://www.gnu.org/licenses/.
*/

using System;
using System.IO;
using System.Threading.Tasks;
using System.Runtime.CompilerServices;

using VNLib.Hashing;
using VNLib.Utils.Memory;
using VNLib.Utils.Logging;
using VNLib.Utils.Resources;
using VNLib.Utils.Extensions;
using VNLib.Data.Caching;
using VNLib.Plugins.Extensions.Loading;
using VNLib.Plugins.Extensions.VNCache.DataModel;

namespace VNLib.Plugins.Extensions.VNCache
{
    /// <summary>
    /// Contains extension methods for aquiring a Plugin managed 
    /// global cache provider.
    /// </summary>
    public static class VNCacheExtensions
    {
        internal const string CACHE_CONFIG_KEY = "cache";
        internal const string EXTERN_CACHE_LIB_PATH = "assembly_name";

        /// <summary>
        /// Loads <see cref="IGlobalCacheProvider"/> from an external asset assembly package
        /// </summary>
        /// <param name="plugin"></param>
        /// <param name="asmDllPath">The path to the assembly that exports the global cache provider instance</param>
        /// <param name="search">The directory search option</param>
        /// <returns>The loaded <see cref="IGlobalCacheProvider"/> instance</returns>
        public static IGlobalCacheProvider LoadCacheLibrary(this PluginBase plugin, string asmDllPath, SearchOption search = SearchOption.AllDirectories) 
             => plugin.CreateServiceExternal<IGlobalCacheProvider>(asmDllPath, search, defaultCtx: null);

        /// <summary>
        /// Gets the configuration assigned global cache provider, if defined. If the configuration does not 
        /// define a cache provider, this method returns null. This method loads a singleton instance.
        /// </summary>
        /// <param name="plugin"></param>
        /// <returns>The assgined global cache provider or null if undefined</returns>
        public static IGlobalCacheProvider? GetDefaultGlobalCache(this PluginBase plugin)
        {
            if (plugin.TryGetConfig(CACHE_CONFIG_KEY) == null)
            {
                return null;
            }

            return LoadingExtensions.GetOrCreateSingleton(plugin, SingletonCacheLoader);
        }

        private static IGlobalCacheProvider SingletonCacheLoader(PluginBase plugin)
        {
            //Get the cache configuration
            IConfigScope config = plugin.GetConfig(CACHE_CONFIG_KEY);

            string dllPath = config.GetRequiredProperty(EXTERN_CACHE_LIB_PATH, p => p.GetString()!);

            plugin.Log.Verbose("Loading external cache library: {cl}", dllPath);

            IGlobalCacheProvider _client = plugin.LoadCacheLibrary(dllPath);

            //Try to call an init method if it exists
            ManagedLibrary.TryGetMethod<Action>(_client, "Init")?.Invoke();

            //Try an async version
            Func<Task>? asyncInit = ManagedLibrary.TryGetMethod<Func<Task>>(_client, "InitAsync");

            //Schedule the async init if it exists
            if (asyncInit != null)
            {
                _ = plugin.ObserveWork(asyncInit, 100);
            }

            return _client;
        }


        /// <summary>
        /// Gets a simple scoped cache based on an entity prefix. The prefix is appended
        /// to the object id on each cache operation
        /// </summary>
        /// <param name="cache"></param>
        /// <param name="prefix">The simple prefix string to append to object ids before computing hashes</param>
        /// <param name="digest">The algorithm used to hash the combined object-ids</param>
        /// <param name="encoding">The string encoding method used to encode the hash output</param>
        /// <returns>The <see cref="ScopedCache"/> instance that will use the prefix to compute object ids</returns>
        /// <exception cref="ArgumentNullException"></exception>
        public static ScopedCache GetPrefixedCache(this IGlobalCacheProvider cache, string prefix, HashAlg digest = HashAlg.SHA1, HashEncodingMode encoding = HashEncodingMode.Base64)
        {
            ArgumentNullException.ThrowIfNull(cache);
            ArgumentException.ThrowIfNullOrEmpty(prefix);
            //Create simple cache key generator
            SimpleCacheKeyImpl keyProv = new(prefix, digest, encoding);
            //Create the scoped cache from the simple provider
            return cache.GetScopedCache(keyProv);
        }

        private sealed class SimpleCacheKeyImpl(string prefix, HashAlg digest, HashEncodingMode encoding) : ICacheKeyGenerator
        {
            private readonly string Prefix = prefix;
            private readonly HashAlg Digest = digest;
            private readonly HashEncodingMode Encoding = encoding;

            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            private int ComputeBufferSize(string id) => id.Length + Prefix.Length;

            string ICacheKeyGenerator.ComputedKey(string entityId)
            {
                //Compute the required character buffer size
                int bufferSize = ComputeBufferSize(entityId);

                if(bufferSize < 128)
                {
                    //Stack alloc a buffer
                    Span<char> buffer = stackalloc char[bufferSize];

                    //Writer to accumulate data
                    ForwardOnlyWriter<char> writer = new(buffer);

                    //Append prefix and entity id
                    writer.AppendSmall(Prefix);
                    writer.AppendSmall(entityId);

                    //Compute the simple hash of the combined values
                    return ManagedHash.ComputeHash(writer.AsSpan(), Digest, Encoding);
                }
                else
                {
                    //Alloc heap buffer for string concatination
                    using UnsafeMemoryHandle<char> buffer = MemoryUtil.UnsafeAlloc<char>(bufferSize, true);

                    //Writer to accumulate data
                    ForwardOnlyWriter<char> writer = new(buffer.Span);

                    //Append prefix and entity id
                    writer.Append(Prefix);
                    writer.Append(entityId);

                    //Compute the simple hash of the combined values
                    return ManagedHash.ComputeHash(writer.AsSpan(), Digest, Encoding);
                }
            }
        }
    }
}
