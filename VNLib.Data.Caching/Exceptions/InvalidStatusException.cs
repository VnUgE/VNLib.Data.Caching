/*
* Copyright (c) 2022 Vaughn Nugent
* 
* Library: VNLib
* Package: VNLib.Data.Caching
* File: InvalidStatusException.cs 
*
* InvalidStatusException.cs is part of VNLib.Data.Caching which is part of the larger 
* VNLib collection of libraries and utilities.
*
* VNLib.Data.Caching is free software: you can redistribute it and/or modify 
* it under the terms of the GNU Affero General Public License as 
* published by the Free Software Foundation, either version 3 of the
* License, or (at your option) any later version.
*
* VNLib.Data.Caching is distributed in the hope that it will be useful,
* but WITHOUT ANY WARRANTY; without even the implied warranty of
* MERCHANTABILITY or FITNESS FOR A PARTICULAR PURPOSE. See the
* GNU Affero General Public License for more details.
*
* You should have received a copy of the GNU Affero General Public License
* along with this program.  If not, see https://www.gnu.org/licenses/.
*/

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
