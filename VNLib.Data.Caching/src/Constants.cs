/*
* Copyright (c) 2022 Vaughn Nugent
* 
* Library: VNLib
* Package: VNLib.Data.Caching
* File: Constants.cs 
*
* Constants.cs is part of VNLib.Data.Caching which is part of the larger 
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

using VNLib.Net.Messaging.FBM;

namespace VNLib.Data.Caching
{
    public static class Constants
    {
        /// <summary>
        /// Contains constants the define actions
        /// </summary>
        public static class Actions
        {
            public const string Get= "g";
            public const string AddOrUpdate = "u";
            public const string Delete = "d";
            public const string Dequeue = "dq";
        }
        /// <summary>
        /// Containts constants for operation response codes
        /// </summary>
        public static class ResponseCodes
        {
            public const string Okay = "ok";
            public const string Error = "err";
            public const string NotFound = "nf";
        }

        public const HeaderCommand ObjectId = (HeaderCommand)0xAA;
        public const HeaderCommand NewObjectId = (HeaderCommand)0xAB;
    }
}
