/*
* Copyright (c) 2024 Vaughn Nugent
* 
* Library: VNLib
* Package: VNLib.Data.Caching
* File: FbmMessageChecksum.cs 
*
* FbmMessageChecksum.cs is part of VNLib.Data.Caching which is part of the larger 
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
using System.Buffers.Binary;
using System.Diagnostics;

using VNLib.Utils;
using VNLib.Hashing.Checksums;
using VNLib.Net.Messaging.FBM;

using static VNLib.Data.Caching.Constants;

namespace VNLib.Data.Caching
{
    /// <summary>
    /// Utility class for verifying and writing checksums for FBM messages
    /// </summary>
    public static class FbmMessageChecksum
    {
        /// <summary>
        /// Verifies the checksum of the supplied data using the FNV1a algorithm
        /// </summary>
        /// <param name="checksum">The checksum base32 encoded string of the checksum data</param>
        /// <param name="data">The data to compute the checksum on</param>
        /// <returns>True if the checksum of the data matches the supplied one</returns>
        public static bool VerifyFnv1aChecksum(ReadOnlySpan<char> checksum, ReadOnlySpan<byte> data)
        {
            //Convert the checksum to bytes
            Span<byte> asBytes = stackalloc byte[sizeof(ulong)];
            ERRNO byteSize = VnEncoding.TryFromBase32Chars(checksum, asBytes);

            Debug.Assert(byteSize == sizeof(ulong), "Failed to convert checksum to bytes");

            //Compute the checksum of the supplied data
            ulong computed = FNV1a.Compute64(data);

            //Compare the checksums
            return BinaryPrimitives.ReadUInt64BigEndian(asBytes) == computed;
        }

        /// <summary>
        /// Writes the FNV1a checksum of the supplied data to the message header buffer
        /// </summary>
        /// <param name="message">The FBM message to write the checksum headers to</param>
        /// <param name="data">The message data to compute the checksum of</param>
        /// <exception cref="ArgumentNullException"></exception>
        public static void WriteFnv1aChecksum(IFBMMessage message, ReadOnlySpan<byte> data)
        {
            ArgumentNullException.ThrowIfNull(message);

            //Compute the checksum of the data
            ulong checksum = FNV1a.Compute64(data);

            Span<byte> asBytes = stackalloc byte[sizeof(ulong)];
            Span<char> asChars = stackalloc char[16];

            //get big endian bytes
            BinaryPrimitives.WriteUInt64BigEndian(asBytes, checksum);
            ERRNO charSize = VnEncoding.TryToBase32Chars(asBytes, asChars);

            Debug.Assert(charSize > 0, "Failed to convert checksum to base32");

            //Write the checksum and type to the response
            message.WriteHeader(ChecksumType, ChecksumTypes.Fnv1a);
            message.WriteHeader(ChecksumValue, asChars[..(int)charSize]);
        }
    }
}
