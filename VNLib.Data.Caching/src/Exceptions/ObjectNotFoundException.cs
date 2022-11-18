using System;

namespace VNLib.Data.Caching.Exceptions
{
    /// <summary>
    /// Raised when a command was executed on a desired object in the remote cache
    /// but the object was not found
    /// </summary>
    public class ObjectNotFoundException : InvalidStatusException
    {
        internal ObjectNotFoundException()
        {}

        internal ObjectNotFoundException(string message) : base(message)
        {}

        internal ObjectNotFoundException(string message, string statusCode) : base(message, statusCode)
        {}

        internal ObjectNotFoundException(string message, Exception innerException) : base(message, innerException)
        {}
    }
}
