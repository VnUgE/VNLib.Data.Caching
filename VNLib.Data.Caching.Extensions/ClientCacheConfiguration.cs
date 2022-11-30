/*
* Copyright (c) 2022 Vaughn Nugent
* 
* Library: VNLib
* Package: VNLib.Data.Caching.Extensions
* File: ClientCacheConfiguration.cs 
*
* ClientCacheConfiguration.cs is part of VNLib.Data.Caching.Extensions which is part of the larger 
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

using System.Security.Cryptography;

using VNLib.Hashing;
using VNLib.Net.Messaging.FBM.Client;

namespace VNLib.Data.Caching.Extensions
{
    /// <summary>
    /// A fluent api configuration object for configuring a <see cref="FBMClient"/>
    /// to connect to cache servers.
    /// </summary>
    public sealed class ClientCacheConfiguration
    {
        internal ECDsa SigningKey { get; init; }
        internal ECDsa VerificationKey { get; init; }
        internal string ServerChallenge { get; init; }
        internal string? NodeId { get; set; }
        internal Uri? BrokerAddress { get; set; }
        internal bool UseTls { get; set; }
        internal ActiveServer[]? CacheServers { get; set; }

        public ClientCacheConfiguration()
        {
            //Init the algorithms
            SigningKey = ECDsa.Create(FBMDataCacheExtensions.CacheCurve);
            VerificationKey = ECDsa.Create(FBMDataCacheExtensions.CacheCurve);
            ServerChallenge = RandomHash.GetRandomBase32(24);
        }

        /// <summary>
        /// Imports the private key used to sign messages
        /// </summary>
        /// <param name="pkcs8PrivKey">The pkcs8 encoded private key to sign messages</param>
        /// <returns>Chainable fluent object</returns>
        /// <exception cref="ArgumentException"></exception>
        /// <exception cref="CryptographicException"></exception>
        public ClientCacheConfiguration ImportSigningKey(ReadOnlySpan<byte> pkcs8PrivKey)
        {
            SigningKey.ImportPkcs8PrivateKey(pkcs8PrivKey, out _);
            return this;
        }

        /// <summary>
        /// Imports the public key used to verify messages from the remote server
        /// </summary>
        /// <param name="spkiPublicKey">The subject-public-key-info formatted cache public key</param>
        /// <returns>Chainable fluent object</returns>
        /// <exception cref="ArgumentException"></exception>
        /// <exception cref="CryptographicException"></exception>
        public ClientCacheConfiguration ImportVerificationKey(ReadOnlySpan<byte> spkiPublicKey)
        {
            VerificationKey.ImportSubjectPublicKeyInfo(spkiPublicKey, out _);
            return this;
        }

        /// <summary>
        /// Specifies if all connections should be using TLS
        /// </summary>
        /// <param name="useTls">A value that indicates if connections should use TLS</param>
        /// <returns>Chainable fluent object</returns>
        public ClientCacheConfiguration WithTls(bool useTls)
        {
            UseTls = useTls;
            return this;   
        }
        /// <summary>
        /// Specifies the broker address to discover cache nodes from
        /// </summary>
        /// <param name="brokerAddress">The address of the server broker</param>
        /// <returns>Chainable fluent object</returns>
        /// <exception cref="ArgumentNullException"></exception>
        public ClientCacheConfiguration WithBroker(Uri brokerAddress)
        {
            this.BrokerAddress = brokerAddress ?? throw new ArgumentNullException(nameof(brokerAddress));
            return this;
        }

        /// <summary>
        /// Specifies the current server's cluster node id. If this 
        /// is a server connection attempting to listen for changes on the
        /// remote server, this id must be set and unique
        /// </summary>
        /// <param name="nodeId">The cluster node id of the current server</param>
        /// <returns>Chainable fluent object</returns>
        /// <exception cref="ArgumentNullException"></exception>
        public ClientCacheConfiguration WithNodeId(string nodeId)
        {
            this.NodeId = nodeId ?? throw new ArgumentNullException(nameof(nodeId));
            return this;
        }

        ~ClientCacheConfiguration()
        {
            SigningKey.Clear();
            VerificationKey.Clear();
        }
    }
}
