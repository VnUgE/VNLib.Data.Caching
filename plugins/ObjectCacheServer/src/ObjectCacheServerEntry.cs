/*
* Copyright (c) 2024 Vaughn Nugent
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

using VNLib.Plugins;
using VNLib.Utils.Logging;
using VNLib.Plugins.Extensions.Loading;
using VNLib.Plugins.Extensions.Loading.Routing;

using VNLib.Data.Caching.ObjectCache.Server.Endpoints;
using VNLib.Data.Caching.ObjectCache.Server.Clustering;

namespace VNLib.Data.Caching.ObjectCache.Server
{

    public sealed class ObjectCacheServerEntry : PluginBase
    {
        public override string PluginName => "ObjectCache.Service";
      

        protected override void OnLoad()
        {
            try
            {
                //Initialize the cache node builder
                ObjectCacheSystemState builder = this.GetOrCreateSingleton<ObjectCacheSystemState>();
                builder.Initialize();

                //Route well-known endpoint
                this.Route<WellKnownEndpoint>();

                //Init connect endpoint
                this.Route<ConnectEndpoint>();

                //We must initialize the replication manager
                _ = this.GetOrCreateSingleton<CacheNodeReplicationMaanger>();

                //Setup discovery endpoint
                if(!string.IsNullOrWhiteSpace(builder.Configuration.DiscoveryPath))
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
            Log.Information("Plugin unloaded");
        }

        protected override void ProcessHostCommand(string cmd)
        {
            throw new NotImplementedException();
        }
    }
}
