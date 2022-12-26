/*
* Copyright (c) 2022 Vaughn Nugent
* 
* Library: VNLib
* Package: VNLib.Data.Caching
* File: MessageTooLargeException.cs 
*
* MessageTooLargeException.cs is part of VNLib.Data.Caching which is part of the larger 
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
