﻿/*
* Copyright (c) 2022 Vaughn Nugent
* 
* Library: VNLib
* Package: VNLib.Data.Caching
* File: ObjectNotFoundException.cs 
*
* ObjectNotFoundException.cs is part of VNLib.Data.Caching which is part of the larger 
* VNLib collection of libraries and utilities.
*
* VNLib.Data.Caching is free software: you can redistribute it and/or modify 
* it under the terms of the GNU General Public License as published
* by the Free Software Foundation, either version 2 of the License,
* or (at your option) any later version.
*
* VNLib.Data.Caching is distributed in the hope that it will be useful,
* but WITHOUT ANY WARRANTY; without even the implied warranty of
* MERCHANTABILITY or FITNESS FOR A PARTICULAR PURPOSE. See the GNU 
* General Public License for more details.
*
* You should have received a copy of the GNU General Public License 
* along with VNLib.Data.Caching. If not, see http://www.gnu.org/licenses/.
*/

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