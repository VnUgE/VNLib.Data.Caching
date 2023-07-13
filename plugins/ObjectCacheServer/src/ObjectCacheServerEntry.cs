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
using System.Threading;
using System.Collections.Generic;

using VNLib.Plugins;
using VNLib.Utils.Memory;
using VNLib.Utils.Logging;
using VNLib.Utils.Memory.Diagnostics;
using VNLib.Plugins.Extensions.Loading;
using VNLib.Plugins.Extensions.Loading.Routing;
using VNLib.Data.Caching.ObjectCache.Server.Endpoints;
using VNLib.Data.Caching.ObjectCache.Server.Distribution;

namespace VNLib.Data.Caching.ObjectCache.Server
{

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
                //Route well-known endpoint
                this.Route<WellKnownEndpoint>();

                //Init connect endpoint
                this.Route<ConnectEndpoint>();

                //We must initialize the replication manager
                _ = this.GetOrCreateSingleton<CacheNodeReplicationMaanger>();

                //Setup discovery endpoint
                if(this.HasConfigForType<PeerDiscoveryEndpoint>())
                {
                    this.Route<PeerDiscoveryEndpoint>();
                }               

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
