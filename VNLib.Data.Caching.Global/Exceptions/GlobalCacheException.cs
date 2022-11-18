namespace VNLib.Data.Caching.Global.Exceptions
{
    public class GlobalCacheException : Exception
    {
        public GlobalCacheException()
        { }
        public GlobalCacheException(string? message) : base(message)
        { }
        public GlobalCacheException(string? message, Exception? innerException) : base(message, innerException)
        { }
    }
}