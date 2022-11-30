/*
* Copyright (c) 2022 Vaughn Nugent
* 
* Library: VNLib
* Package: VNLib.Data.Caching.Extensions
* File: BrokerRegistrationRequest.cs 
*
* BrokerRegistrationRequest.cs is part of VNLib.Data.Caching.Extensions which is part of the larger 
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

using VNLib.Utils;


namespace VNLib.Data.Caching.Extensions
{
    /// <summary>
    /// A broker registration request message in a fluent api
    /// format. This message may be disposed when no longer in use
    /// </summary>
    public sealed class BrokerRegistrationRequest : VnDisposeable
    {
        /// <summary>
        /// The cache server node id
        /// </summary>
        public string? NodeId { get; private set; }
        /// <summary>
        /// The broker server's address
        /// </summary>
        public Uri? BrokerAddress { get; private set; }
        /// <summary>
        /// The security token used by the broker server to 
        /// authenticate during heartbeat connections
        /// </summary>
        public string? HeartbeatToken { get; private set; }
        /// <summary>
        /// The address for remote clients to use to 
        /// connect to this server
        /// </summary>
        public string? RegistrationAddress { get; private set; }
        /// <summary>
        /// The token signature algorithm
        /// </summary>
        public ECDsa SiginingAlg { get; }

        public BrokerRegistrationRequest()
        {
            SiginingAlg = ECDsa.Create(FBMDataCacheExtensions.CacheCurve);
        }

        public BrokerRegistrationRequest WithPrivateKey(ReadOnlySpan<byte> privateKey)
        {
            SiginingAlg.ImportPkcs8PrivateKey(privateKey, out _);
            return this;
        }

        public BrokerRegistrationRequest WithBroker(Uri brokerUri)
        {
            BrokerAddress = brokerUri;
            return this;
        }

        public BrokerRegistrationRequest WithRegistrationAddress(string address)
        {
            RegistrationAddress = address;
            return this;
        }

        public BrokerRegistrationRequest WithHeartbeatToken(string token)
        {
            HeartbeatToken = token;
            return this;
        }

        public BrokerRegistrationRequest WithNodeId(string nodeId)
        {
            NodeId = nodeId;
            return this;
        }

        ///<inheritdoc/>
        protected override void Free()
        {
            SiginingAlg.Dispose();
        }
    }
}
