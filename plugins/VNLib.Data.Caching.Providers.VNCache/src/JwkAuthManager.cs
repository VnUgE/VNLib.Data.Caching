/*
* Copyright (c) 2025 Vaughn Nugent
* 
* Library: VNLib
* Package: VNLib.Data.Caching.Providers.VNCache
* File: JwkAuthManager.cs 
*
* JwkAuthManager.cs is part of VNLib.Data.Caching.Providers.VNCache which is part of the larger 
* VNLib collection of libraries and utilities.
*
* VNLib.Data.Caching.Providers.VNCache is free software: you can redistribute it and/or modify 
* it under the terms of the GNU Affero General Public License as 
* published by the Free Software Foundation, either version 3 of the
* License, or (at your option) any later version.
*
* VNLib.Data.Caching.Providers.VNCache is distributed in the hope that it will be useful,
* but WITHOUT ANY WARRANTY; without even the implied warranty of
* MERCHANTABILITY or FITNESS FOR A PARTICULAR PURPOSE. See the
* GNU Affero General Public License for more details.
*
* You should have received a copy of the GNU Affero General Public License
* along with this program.  If not, see https://www.gnu.org/licenses/.
*/

using System;
using System.Threading.Tasks;
using System.Collections.Generic;
using System.Security.Cryptography;

using VNLib.Hashing;
using VNLib.Hashing.IdentityUtility;
using VNLib.Data.Caching.Extensions;
using VNLib.Plugins.Extensions.Loading;

namespace VNLib.Data.Caching.Providers.VNCache
{
    /// <summary>
    /// Manages JSON Web Key (JWK) based authentication for signing and verifying messages
    /// for cache clients
    /// </summary>
    public sealed class JwkAuthManager : ICacheAuthManager
    {
        private readonly IAsyncLazy<ReadOnlyJsonWebKey> _sigKey;
        private readonly IAsyncLazy<ReadOnlyJsonWebKey> _verKey;

        private JwkAuthManager(
            IAsyncLazy<ReadOnlyJsonWebKey> sigKey, 
            IAsyncLazy<ReadOnlyJsonWebKey> verifKey
        )
        {
            _sigKey = sigKey;
            _verKey = verifKey;
        }

        /// <summary>
        /// Creates a new instance of <see cref="JwkAuthManager"/> from the given keys
        /// that are loaded lazily.
        /// </summary>
        /// <param name="sigKey">The key used to sign client messages.</param>
        /// <param name="verifKey">The key used to verify server messages.</param>
        /// <returns>The new <see cref="JwkAuthManager"/>.</returns>
        /// <exception cref="ArgumentNullException"></exception>
        public static JwkAuthManager FromLazyJwk(
            IAsyncLazy<ReadOnlyJsonWebKey> sigKey,
            IAsyncLazy<ReadOnlyJsonWebKey> verifKey
        )
        {
            ArgumentNullException.ThrowIfNull(sigKey);
            ArgumentNullException.ThrowIfNull(verifKey);

            return new(sigKey, verifKey);
        }

        /// <summary>
        /// Creates a new instance of <see cref="JwkAuthManager"/> from the given keys.
        /// </summary>
        /// <param name="sigKey">The key used to sign client messages.</param>
        /// <param name="verifKey">The key used to verify server messages.</param>
        /// <returns>The new <see cref="JwkAuthManager"/>.</returns>
        /// <exception cref="ArgumentNullException"></exception>
        public static JwkAuthManager FromJwk(
           ReadOnlyJsonWebKey sigKey,
           ReadOnlyJsonWebKey verifKey
        )
        {
            ArgumentNullException.ThrowIfNull(sigKey);
            ArgumentNullException.ThrowIfNull(verifKey);

            return FromLazyJwk(
                sigKey: Task.FromResult(sigKey).AsLazy(),
                verifKey: Task.FromResult(verifKey).AsLazy()
            );
        }

        /// <summary>
        /// Creates a new instance of <see cref="JwkAuthManager"/> from JSON string keys in JWK format.
        /// </summary>
        /// <param name="sigKey">The JSON string of the key used to sign client messages in JWK format.</param>
        /// <param name="verifKey">The JSON string of the key used to verify server messages in JWK format.</param>
        /// <returns>The new <see cref="JwkAuthManager"/>.</returns>
        /// <exception cref="ArgumentNullException"></exception>
        public static JwkAuthManager FromJwkStrings(string sigKey, string verifKey)
        {
            ArgumentNullException.ThrowIfNull(sigKey);
            ArgumentNullException.ThrowIfNull(verifKey);

            return FromJwk(
                sigKey: ReadOnlyJsonWebKey.FromJsonString(sigKey),
                verifKey: ReadOnlyJsonWebKey.FromJsonString(verifKey)
            );
        }

        /// <summary>
        /// Creates a new instance of <see cref="JwkAuthManager"/> from byte array keys in JWK format.
        /// </summary>
        /// <param name="sigKey">The byte array of the key used to sign client messages in JWK format.</param>
        /// <param name="verifKey">The byte array of the key used to verify server messages in JWK format.</param>
        /// <returns>The new <see cref="JwkAuthManager"/>.</returns>
        /// <exception cref="ArgumentNullException"></exception>
        public static JwkAuthManager FromJwkBytes(ReadOnlySpan<byte> sigKey, ReadOnlySpan<byte> verifKey)
        {
            return FromJwk(
                sigKey: ReadOnlyJsonWebKey.FromUtf8Bytes(sigKey),
                verifKey: ReadOnlyJsonWebKey.FromUtf8Bytes(verifKey)
            );
        }

        /// <summary>
        /// Waits for the lazy keys load to complete.
        /// </summary>
        /// <returns>A task that represents the asynchronous operation.</returns>
        internal async Task AwaitLazyKeyLoad()
        {
            await _sigKey;
            await _verKey;
        }

        ///<inheritdoc/>
        public IReadOnlyDictionary<string, string?> GetJwtHeader()
            => _sigKey.Value.JwtHeader;

        ///<inheritdoc/>
        public void SignJwt(JsonWebToken jwt)
            => jwt.SignFromJwk(_sigKey.Value);

        ///<inheritdoc/>
        public byte[] SignMessageHash(byte[] hash, HashAlg alg)
        {
            //try to get the rsa alg for the signing key
            using RSA? rsa = _sigKey.Value.GetRSAPrivateKey();
            if (rsa != null)
            {
                return rsa.SignHash(hash, alg.GetAlgName(), RSASignaturePadding.Pkcs1);
            }

            //try to get the ecdsa alg for the signing key
            using ECDsa? ecdsa = _sigKey.Value.GetECDsaPrivateKey();
            if (ecdsa != null)
            {
                return ecdsa.SignHash(hash);
            }

            throw new NotSupportedException("The signing key is not a valid RSA or ECDSA key");
        }

        ///<inheritdoc/>
        public bool VerifyJwt(JsonWebToken jwt, bool isPeer)
        {
            return jwt.VerifyFromJwk(_verKey.Value);
        }

        ///<inheritdoc/>
        public bool VerifyMessageHash(ReadOnlySpan<byte> hash, HashAlg alg, ReadOnlySpan<byte> signature, bool isPeer)
        {
            //try to get the rsa alg for the signing key
            using RSA? rsa = _verKey.Value.GetRSAPublicKey();
            if (rsa != null)
            {
                return rsa.VerifyHash(
                    hash,
                    signature,
                    hashAlgorithm: alg.GetAlgName(),
                    RSASignaturePadding.Pkcs1
                );
            }

            //try to get the ecdsa alg for the signing key
            using ECDsa? ecdsa = _verKey.Value.GetECDsaPublicKey();
            if (ecdsa != null)
            {
                return ecdsa.VerifyHash(hash, signature);
            }

            throw new NotSupportedException("The current key is not an RSA or ECDSA key and is not supported");
        }
    }
}