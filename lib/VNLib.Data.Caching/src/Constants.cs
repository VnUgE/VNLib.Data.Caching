/*
* Copyright (c) 2024 Vaughn Nugent
* 
* Library: VNLib
* Package: VNLib.Data.Caching
* File: Constants.cs 
*
* Constants.cs is part of VNLib.Data.Caching which is part of the larger 
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
            public const string InvalidChecksum = "cm";
        }

        public const HeaderCommand ObjectId = (HeaderCommand)0xAA;
        public const HeaderCommand NewObjectId = (HeaderCommand)0xAB;
        public const HeaderCommand ChecksumType = (HeaderCommand)0xA1;
        public const HeaderCommand ChecksumValue = (HeaderCommand)0xA2;
        public const HeaderCommand ChecksumWarning = (HeaderCommand)0xA3;

        /// <summary>
        /// Contains constants for checksum type names for FBM headers
        /// </summary>
        public static class ChecksumTypes
        {
            public const string Fnv1a = "fnv1a";
            public const string Crc32 = "crc32";
            public const string Md5 = "md5";
            public const string Sha1 = "sha1";
            public const string Sha256 = "sha256";
        }
    }
}
