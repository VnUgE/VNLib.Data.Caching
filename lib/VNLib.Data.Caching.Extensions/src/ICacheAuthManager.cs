/*
* Copyright (c) 2023 Vaughn Nugent
* 
* Library: VNLib
* Package: VNLib.Data.Caching.Extensions
* File: ClientCacheConfiguration.cs 
*
* ClientCacheConfiguration.cs is part of VNLib.Data.Caching.Extensions which is part of the larger 
* VNLib collection of libraries and utilities.
*
* VNLib.Data.Caching.Extensions is free software: you can redistribute it and/or modify 
* it under the terms of the GNU Affero General Public License as 
* published by the Free Software Foundation, either version 3 of the
* License, or (at your option) any later version.
*
* VNLib.Data.Caching.Extensions is distributed in the hope that it will be useful,
* but WITHOUT ANY WARRANTY; without even the implied warranty of
* MERCHANTABILITY or FITNESS FOR A PARTICULAR PURPOSE. See the
* GNU Affero General Public License for more details.
*
* You should have received a copy of the GNU Affero General Public License
* along with this program.  If not, see https://www.gnu.org/licenses/.
*/

using System;
using System.Collections.Generic;

using VNLib.Hashing;
using VNLib.Hashing.IdentityUtility;

namespace VNLib.Data.Caching.Extensions
{
    /// <summary>
    /// Provides authentication services for cache clients and 
    /// servers.
    /// </summary>
    public interface ICacheAuthManager
    {
        /// <summary>
        /// Gets the JWT header to use for signing messages with the 
        /// given key
        /// </summary>
        /// <returns>The JWT header collection</returns>
        IReadOnlyDictionary<string, string?> GetJwtHeader();

        /// <summary>
        /// Signs the given JWT
        /// </summary>
        /// <param name="jwt">The message to sign</param>
        void SignJwt(JsonWebToken jwt);

        /// <summary>
        /// Verifies the given JWT
        /// </summary>
        /// <param name="jwt">The message to verify authenticity</param>
        /// <param name="isPeer">A value indicating if the message is from a known node</param>
        /// <returns>True of the JWT could be verified, false otherwise</returns>
        bool VerifyJwt(JsonWebToken jwt, bool isPeer);

        /// <summary>
        /// Signs the given message hash
        /// </summary>
        /// <param name="hash">The message hash to sign</param>
        /// <param name="alg">The algorithm used to sign the message hash</param>
        /// <returns>The signature of the hash</returns>
        byte[] SignMessageHash(byte[] hash, HashAlg alg);

        /// <summary>
        /// Verifies the given message hash against the signature.
        /// </summary>
        /// <param name="hash">The message hash to compare</param>
        /// <param name="alg">The algorithm used to produce the message hash</param>
        /// <param name="signature">The message signature to verify the message against</param>
        /// <param name="isPeer">A value indicating if the message is from a known node</param>
        /// <returns>True of the signature could be verified</returns>
        bool VerifyMessageHash(ReadOnlySpan<byte> hash, HashAlg alg, ReadOnlySpan<byte> signature, bool isPeer);
    }
}
