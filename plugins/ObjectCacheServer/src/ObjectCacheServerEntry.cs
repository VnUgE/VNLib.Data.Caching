/*
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
using System.IO;
using System.Net;
using System.Linq;
using System.Net.Http;
using System.Threading;
using System.Net.Sockets;
using System.Threading.Tasks;
using System.Collections.Generic;
using System.Security.Cryptography;

using VNLib.Plugins;
using VNLib.Utils.Memory;
using VNLib.Utils.Logging;
using VNLib.Utils.Memory.Diagnostics;
using VNLib.Hashing.IdentityUtility;
using VNLib.Data.Caching.Extensions;
using static VNLib.Data.Caching.Constants;
using VNLib.Net.Messaging.FBM;
using VNLib.Net.Messaging.FBM.Client;
using VNLib.Plugins.Cache.Broker.Endpoints;
using VNLib.Plugins.Extensions.Loading;
using VNLib.Plugins.Extensions.Loading.Routing;
using VNLib.Data.Caching.ObjectCache.Server.Endpoints;

namespace VNLib.Data.Caching.ObjectCache.Server
{
    sealed record class CacheAuthKeyStore(PluginBase Plugin)
    {
        public Task<ReadOnlyJsonWebKey> GetCachePublicAsync()
        {
            return Plugin.TryGetSecretAsync("cache_private_key").ToJsonWebKey(true);
        }

        public Task<ReadOnlyJsonWebKey> GetCachePrivateAsync()
        {
            return Plugin.TryGetSecretAsync("cache_private_key").ToJsonWebKey(true);
        }

        public Task<ReadOnlyJsonWebKey> GetBrokerPublicAsync()
        {
            return Plugin.TryGetSecretAsync("broker_public_key").ToJsonWebKey(true);
        }

        public Task<ReadOnlyJsonWebKey> GetClientPublicKeyAsync()
        {
            return Plugin.TryGetSecretAsync("client_public_key").ToJsonWebKey(true);
        }
    }

    internal interface IBrokerHeartbeatNotifier
    {
        /// <summary>
        /// Called when the heartbeat endpoint receives a heartbeat from the broker
        /// </summary>
        void HearbeatReceived();

        /// <summary>
        /// Gets the current auth token sent to the broker, which is expected to be sent back in the heartbeat
        /// </summary>
        /// <returns>The heartbeat auth token if set</returns>
        string? GetAuthToken();

        /// <summary>
        /// Gets the address of the broker server
        /// </summary>
        /// <returns>The full address of the broker server to connect to</returns>
        Uri GetBrokerAddress();

        /// <summary>
        /// Gets the public key of the broker server
        /// </summary>
        /// <returns>The broker's public key</returns>
        ReadOnlyJsonWebKey GetBrokerPublicKey();
    }

    public sealed class ObjectCacheServerEntry : PluginBase
    {
        public override string PluginName => "ObjectCache.Service";

        private readonly Lazy<IUnmangedHeap> _cacheHeap;   

        /// <summary>
        /// Gets the shared heap for the plugin 
        /// </summary>
        internal IUnmangedHeap CacheHeap => _cacheHeap.Value;

        public ObjectCacheServerEntry()
        {
            //Init heap
            _cacheHeap = new Lazy<IUnmangedHeap>(InitializeHeap, LazyThreadSafetyMode.PublicationOnly);
        }

        private IUnmangedHeap InitializeHeap()
        {
            //Create default heap
            IUnmangedHeap _heap = MemoryUtil.InitializeNewHeapForProcess();
            try
            {
                //If the plugin is in debug mode enable heap tracking
                return this.IsDebug() ? new TrackedHeapWrapper(_heap) : _heap;
            }
            catch
            {
                _heap.Dispose();
                throw;
            }
        }


        protected override void OnLoad()
        {
            try
            {
                //Setup Node config 
                NodeConfig nodeConf = this.GetOrCreateSingleton<NodeConfig>();

                //Init connect endpoint
                ConnectEndpoint endpoint = this.Route<ConnectEndpoint>();

                //Route the broker endpoint
                this.Route<BrokerHeartBeatEndpoint>();

                //Setup discovery endpoint
                if(this.HasConfigForType<PeerDiscoveryEndpoint>())
                {
                    this.Route<PeerDiscoveryEndpoint>();
                }

                ulong maxByteSize = ((ulong)endpoint.CacheConfig.MaxCacheEntries * (ulong)endpoint.CacheConfig.BucketCount * (ulong)endpoint.CacheConfig.MaxMessageSize);

                //Log max memory usage
                Log.Debug("Maxium memory consumption {mx}Mb", maxByteSize / (ulong)(1024 * 1000));
               

                Log.Information("Plugin loaded");
            }
            catch (KeyNotFoundException kne)
            {
                Log.Error("Missing required configuration variables {m}", kne.Message);
            }
        }

        protected override void OnUnLoad()
        {
            //dispose heap if initialized
            if(_cacheHeap.IsValueCreated)
            {
                _cacheHeap.Value.Dispose();
            }

            Log.Information("Plugin unloaded");
        }

        protected override void ProcessHostCommand(string cmd)
        {
            throw new NotImplementedException();
        }
    }
}
