using System;
using System.Runtime.Serialization;

using VNLib.Net.Messaging.FBM;

namespace VNLib.Data.Caching.Exceptions
{
    /// <summary>
    /// Raised when a request (or server response) calculates the size of the message to be too large to proccess
    /// </summary>
    public class MessageTooLargeException : FBMException
    {
        ///<inheritdoc/>
        public MessageTooLargeException()
        {}
        ///<inheritdoc/>
        public MessageTooLargeException(string message) : base(message)
        {}
        ///<inheritdoc/>
        public MessageTooLargeException(string message, Exception innerException) : base(message, innerException)
        {}
        ///<inheritdoc/>
        protected MessageTooLargeException(SerializationInfo info, StreamingContext context) : base(info, context)
        {}
    }
}
