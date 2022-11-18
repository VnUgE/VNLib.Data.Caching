namespace VNLib.Data.Caching.Global.Exceptions
{
    public class CacheNotLoadedException : GlobalCacheException
    {
        public CacheNotLoadedException()
        { }

        public CacheNotLoadedException(string? message) : base(message)
        { }

        public CacheNotLoadedException(string? message, Exception? innerException) : base(message, innerException)
        { }
    }
}