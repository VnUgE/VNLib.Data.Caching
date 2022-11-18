using VNLib.Data.Caching.Global.Exceptions;

namespace VNLib.Data.Caching.Global
{
    /// <summary>
    /// A static library for caching data in-process or a remote data 
    /// cache
    /// </summary>
    public static class GlobalDataCache
    {

        private static IGlobalCacheProvider? CacheProvider;
        private static CancellationTokenRegistration _reg;

        private static readonly object CacheLock = new();
        private static readonly Dictionary<string, WeakReference<object>> LocalCache = new();

        /// <summary>
        /// Gets a value that indicates if global cache is available
        /// </summary>
        public static bool IsAvailable => CacheProvider != null && CacheProvider.IsConnected;

        /// <summary>
        /// Sets the backing cache provider for the process-wide global cache
        /// </summary>
        /// <param name="cacheProvider">The cache provider instance</param>
        /// <param name="statusToken">A token that represents the store's validity</param>
        public static void SetProvider(IGlobalCacheProvider cacheProvider, CancellationToken statusToken)
        {
            CacheProvider = cacheProvider ?? throw new ArgumentNullException(nameof(cacheProvider));
            //Remove cache provider when cache provider is no longer valid
            _reg = statusToken.Register(Cleanup);
        }

        private static void Cleanup()
        {
            CacheProvider = null;
            //Clear local cache
            lock (CacheLock)
            {
                LocalCache.Clear();
            }
            _reg.Dispose();
        }

        /// <summary>
        /// Asynchronously gets a value from the global cache provider 
        /// </summary>
        /// <typeparam name="T"></typeparam>
        /// <param name="key">The key identifying the object to recover from cache</param>
        /// <returns>The value if found, or null if it does not exist in the store</returns>
        /// <exception cref="GlobalCacheException"></exception>
        /// <exception cref="CacheNotLoadedException"></exception>
        public static async Task<T?> GetAsync<T>(string key) where T: class
        {            
            //Check local cache first
            lock (CacheLock)
            {
                if (LocalCache.TryGetValue(key, out WeakReference<object>? wr))
                {
                    //Value is found
                    if(wr.TryGetTarget(out object? value))
                    {
                        //Value exists and is loaded to local cache
                        return (T)value;
                    }
                    //Value has been collected
                    else
                    {
                        //Remove the key from the table
                        LocalCache.Remove(key);
                    }
                }
            }
            //get ref to local cache provider
            IGlobalCacheProvider? prov = CacheProvider;
            if(prov == null)
            {
                throw new CacheNotLoadedException("Global cache provider was not found");
            }
            //get the value from the store
            T? val = await prov.GetAsync<T>(key);
            //Only store the value if it was successfully found
            if (val != null)
            {
                //Store in local cache
                lock (CacheLock)
                {
                    LocalCache[key] = new WeakReference<object>(val);
                }
            }
            return val;
        }

        /// <summary>
        /// Asynchronously sets (or updates) a cached value in the global cache
        /// </summary>
        /// <typeparam name="T"></typeparam>
        /// <param name="key">The key identifying the object to recover from cache</param>
        /// <param name="value">The value to set at the given key</param>
        /// <returns>A task that completes when the update operation has compelted</returns>
        /// <exception cref="GlobalCacheException"></exception>
        /// <exception cref="CacheNotLoadedException"></exception>
        public static async Task SetAsync<T>(string key, T value) where T : class
        {
            //Local record is stale, allow it to be loaded from cache next call to get
            lock (CacheLock)
            {
                LocalCache.Remove(key);
            }
            //get ref to local cache provider
            IGlobalCacheProvider? prov = CacheProvider;
            if (prov == null)
            {
                throw new CacheNotLoadedException("Global cache provider was not found");
            }
            //set the value in the store
            await prov.SetAsync<T>(key, value);
        }

        /// <summary>
        /// Asynchronously deletes an item from cache by its key
        /// </summary>
        /// <param name="key"></param>
        /// <returns>A task that completes when the delete operation has compelted</returns>
        /// <exception cref="GlobalCacheException"></exception>
        /// <exception cref="CacheNotLoadedException"></exception>
        public static async Task DeleteAsync(string key)
        {
            //Delete from local cache 
            lock (CacheLock)
            {
                LocalCache.Remove(key);
            }
            //get ref to local cache provider
            IGlobalCacheProvider? prov = CacheProvider;
            if (prov == null)
            {
                throw new CacheNotLoadedException("Global cache provider was not found");
            }
            //Delete value from store
            await prov.DeleteAsync(key);
        }
    }
}