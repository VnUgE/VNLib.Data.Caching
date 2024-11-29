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

using VNLib.Plugins;
using VNLib.Utils;
using VNLib.Utils.Logging;
using VNLib.Plugins.Extensions.Loading;
using VNLib.Plugins.Extensions.Loading.Routing;

using VNLib.Data.Caching.ObjectCache.Server.Endpoints;
using VNLib.Data.Caching.ObjectCache.Server.Clustering;
using VNLib.Data.Caching.ObjectCache.Server.Rpc;
using VNLib.Plugins.Extensions.Loading.Routing.Mvc;

namespace VNLib.Data.Caching.ObjectCache.Server
{

    public sealed class ObjectCacheServerEntry : PluginBase
    {
        public override string PluginName => "ObjectCache.Service";

        ObjectCacheSystemState? sysState;

        protected override void OnLoad()
        {
            //Initialize the cache node builder
            sysState = this.GetOrCreateSingleton<ObjectCacheSystemState>();
            sysState.Initialize();

            //Route the rpc controller if the user has configured it
            if (this.HasConfigForType<CacheRpcController>())
            {
                this.Route<CacheRpcController>();
            }
            else
            {
                Log.Warn("No rpc configuration was loaded, your server will not be able to communicate with clients");
            }

            //Route well-known endpoint
            this.Route<WellKnownEndpoint>();

            //Init connect endpoint
            this.Route<ConnectEndpoint>();

            //We must initialize the replication manager
            _ = this.GetOrCreateSingleton<CacheNodeReplicationMaanger>();

            Log.Information("Plugin loaded");
        }

        protected override void OnUnLoad()
        {
            Log.Information("Plugin unloaded");
        }

        protected override void ProcessHostCommand(string cmd)
        {
            if(string.IsNullOrWhiteSpace(cmd))
            {
                return;
            }

            ArgumentList al = new(cmd.Split(" "));

            if(al.Count == 0)
            {
                Log.Warn("Invalid command");
                return;
            }

            switch (al[0].ToLower(null))
            {
                case "memstats":
                    sysState?.LogMemoryStats();
                    break;

                default:
                    Log.Warn("Invalid command");
                    break;
            }
        }
    }
}
