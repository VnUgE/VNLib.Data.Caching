/*
* Copyright (c) 2022 Vaughn Nugent
* 
* Library: VNLib
* Package: VNLib.Data.Caching.Extensions
* File: FBMServerNegiationException.cs 
*
* FBMServerNegiationException.cs is part of VNLib.Data.Caching.Extensions which is part of the larger 
* VNLib collection of libraries and utilities.
*
* VNLib.Data.Caching.Extensions is free software: you can redistribute it and/or modify 
* it under the terms of the GNU General Public License as published
* by the Free Software Foundation, either version 2 of the License,
* or (at your option) any later version.
*
* VNLib.Data.Caching.Extensions is distributed in the hope that it will be useful,
* but WITHOUT ANY WARRANTY; without even the implied warranty of
* MERCHANTABILITY or FITNESS FOR A PARTICULAR PURPOSE. See the GNU 
* General Public License for more details.
*
* You should have received a copy of the GNU General Public License 
* along with VNLib.Data.Caching.Extensions. If not, see http://www.gnu.org/licenses/.
*/

using VNLib.Net.Messaging.FBM;


namespace VNLib.Data.Caching.Extensions
{
    /// <summary>
    /// Represents an exception that is raised because of a client-server
    /// negotiation failure.
    /// </summary>
    public class FBMServerNegiationException : FBMException
    {
        public FBMServerNegiationException()
        {}
        public FBMServerNegiationException(string message) : base(message)
        {}
        public FBMServerNegiationException(string message, Exception innerException) : base(message, innerException)
        {}
    }
}
