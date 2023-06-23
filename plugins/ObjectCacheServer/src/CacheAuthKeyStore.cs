﻿/*
* Copyright (c) 2023 Vaughn Nugent
* 
* Library: VNLib
* Package: ObjectCacheServer
* File: ObjectCacheServerEntry.cs 
*
* ObjectCacheServerEntry.cs is part of ObjectCacheServer which is part of the larger 
* VNLib collection of libraries and utilities.
*
* ObjectCacheServer is free software: you can redistribute it and/or modify 
* it under the terms of the GNU Affero General Public License as 
* published by the Free Software Foundation, either version 3 of the
* License, or (at your option) any later version.
*
* ObjectCacheServer is distributed in the hope that it will be useful,
* but WITHOUT ANY WARRANTY; without even the implied warranty of
* MERCHANTABILITY or FITNESS FOR A PARTICULAR PURPOSE. See the
* GNU Affero General Public License for more details.
*
* You should have received a copy of the GNU Affero General Public License
* along with this program.  If not, see https://www.gnu.org/licenses/.
*/

using System;
using System.Collections.Generic;
using System.Security.Cryptography;

using VNLib.Hashing;
using VNLib.Plugins;
using VNLib.Hashing.IdentityUtility;
using VNLib.Plugins.Extensions.Loading;
using VNLib.Data.Caching.Extensions;


namespace VNLib.Data.Caching.ObjectCache.Server
{
    sealed record class CacheAuthKeyStore : ICacheAuthManager
    {
        private readonly IAsyncLazy<ReadOnlyJsonWebKey> _clientPub;
        private readonly IAsyncLazy<ReadOnlyJsonWebKey> _cachePriv;

        public CacheAuthKeyStore(PluginBase plugin)
        {
            _clientPub = plugin.GetSecretAsync("client_public_key").ToLazy(r => r.GetJsonWebKey());
            _cachePriv = plugin.GetSecretAsync("cache_private_key").ToLazy(r => r.GetJsonWebKey());
        }

        ///<inheritdoc/>
        public IReadOnlyDictionary<string, string?> GetJwtHeader()
        {
            return _cachePriv.Value.JwtHeader;
        }

        ///<inheritdoc/>
        public void SignJwt(JsonWebToken jwt)
        {
            jwt.SignFromJwk(_cachePriv.Value);
        }

        ///<inheritdoc/>
        public bool VerifyJwt(JsonWebToken jwt)
        {
            return jwt.VerifyFromJwk(_clientPub.Value);
        }

        /// <summary>
        /// Verifies the message against the stored cache key
        /// </summary>
        /// <param name="jwt">The token to verify</param>
        /// <returns>True if the token was verified, false otherwise</returns>
        public bool VerifyCachePeer(JsonWebToken jwt)
        {
            return jwt.VerifyFromJwk(_cachePriv.Value);
        }

        ///<inheritdoc/>
        public byte[] SignMessageHash(byte[] hash, HashAlg alg)
        {
            //try to get the rsa alg for the signing key
            using RSA? rsa = _cachePriv.Value.GetRSAPublicKey();
            if (rsa != null)
            {
                return rsa.SignHash(hash, alg.GetAlgName(), RSASignaturePadding.Pkcs1);
            }

            //try to get the ecdsa alg for the signing key
            using ECDsa? ecdsa = _cachePriv.Value.GetECDsaPublicKey();
            if (ecdsa != null)
            {
                return ecdsa.SignHash(hash);
            }

            throw new NotSupportedException("The signing key is not a valid RSA or ECDSA key");
        }
      
        ///<inheritdoc/>
        public bool VerifyMessageHash(ReadOnlySpan<byte> hash, HashAlg alg, ReadOnlySpan<byte> signature)
        {
            //try to get the rsa alg for the signing key
            using RSA? rsa = _clientPub.Value.GetRSAPublicKey();
            if (rsa != null)
            {
                return rsa.VerifyHash(hash, signature, alg.GetAlgName(), RSASignaturePadding.Pkcs1);
            }

            //try to get the ecdsa alg for the signing key
            using ECDsa? ecdsa = _clientPub.Value.GetECDsaPublicKey();
            if (ecdsa != null)
            {
                return ecdsa.VerifyHash(hash, signature);
            }

            throw new NotSupportedException("The current key is not an RSA or ECDSA key and is not supported");
        }
    }
}
