/*
* Copyright (c) 2026 Vaughn Nugent
*
* Library: VNLib
* Package: VNLib.Data.Caching.IntegrationTests
* File: CacheProviderTestPlugin.cs 
*
* CacheProviderTestPlugin.cs is part of VNLib.Data.Caching.IntegrationTests which is part of 
* the larger VNLib collection of libraries and utilities.
*
* VNLib.Data.Caching.IntegrationTests is free software: you can redistribute it and/or modify 
* it under the terms of the GNU Affero General Public License as 
* published by the Free Software Foundation, either version 3 of the
* License, or (at your option) any later version.
*
* VNLib.Data.Caching.IntegrationTests is distributed in the hope that it will be useful,
* but WITHOUT ANY WARRANTY; without even the implied warranty of
* MERCHANTABILITY or FITNESS FOR A PARTICULAR PURPOSE. See the
* GNU Affero General Public License for more details.
*
* You should have received a copy of the GNU Affero General Public License
* along with this program.  If not, see https://www.gnu.org/licenses/.
*/

using System;

using VNLib.Utils.Logging;
using VNLib.Plugins;
using VNLib.Plugins.Extensions.VNCache;
using VNLib.Plugins.Extensions.Loading;

namespace VNLib.Data.Caching.IntegrationTests.TestPlugins
{
    /// <summary>
    /// A minimal test plugin that loads a cache provider via <see cref="VNCacheExtensions.GetDefaultGlobalCache"/>
    /// and exports the <see cref="ICacheClient"/> for testing.
    /// </summary>
    public sealed class CacheProviderTestPlugin : PluginBase
    {
        public override string PluginName => "CacheProviderTestPlugin";

        protected override void OnLoad()
        {
            // Load the cache client using the VNCacheExtensions method
            // This will read the "cache" configuration block and dynamically load
            // the provider assembly specified by "assembly_name"
            ICacheClient? cacheClient = this.GetDefaultGlobalCache() 
                ?? throw new Exception("Global cache failed to find and load a provider library");

            this.ExportService(cacheClient, ExportFlags.None);

            Log.Information("Cache provider loaded successfully");
        }

        protected override void OnUnLoad()
        {
            Log.Information("Cache provider unloading");
        }

        protected override void ProcessHostCommand(string cmd) { }        
       
    }
}
