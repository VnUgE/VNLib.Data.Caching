using System;

using VNLib.Net.Messaging.FBM;

namespace VNLib.Data.Caching.Exceptions
{
    /// <summary>
    /// Raised when the response status code of an FBM Request message is not valid for 
    /// the specified request
    /// </summary>
    public class InvalidStatusException : InvalidResponseException
    {
        private readonly string? StatusCode;
        /// <summary>
        /// Initalizes a new <see cref="InvalidStatusException"/> with the specfied status code
        /// </summary>
        /// <param name="message"></param>
        /// <param name="statusCode"></param>
        public InvalidStatusException(string message, string statusCode):this(message)
        {
            this.StatusCode = statusCode;
        }

        ///<inheritdoc/>
        public InvalidStatusException()
        {
        }
        ///<inheritdoc/>
        public InvalidStatusException(string message) : base(message)
        {
        }
        ///<inheritdoc/>
        public InvalidStatusException(string message, Exception innerException) : base(message, innerException)
        {
        }
        ///<inheritdoc/>
        public override string Message => $"InvalidStatusException: Status Code {StatusCode} \r\n {base.Message}";
    }
}
