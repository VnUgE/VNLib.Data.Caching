/*
* Copyright (c) 2026 Vaughn Nugent
*
* Library: VNLib
* Package: VNLib.Data.Caching.IntegrationTests
* File: RedisProviderTests.cs 
*
* RedisProviderTests.cs is part of VNLib.Data.Caching.IntegrationTests which is part of 
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
using System.Text.Json;
using System.Threading.Tasks;

using Microsoft.VisualStudio.TestTools.UnitTesting;

using VNLib.Utils.Resources;
using VNLib.Plugins.Essentials.ServiceStack.Testing;
using VNLib.Data.Caching.IntegrationTests.TestPlugins;

namespace VNLib.Data.Caching.IntegrationTests.Tests.Dynamic
{
    /// <summary>
    /// Integration tests for the Redis provider loaded via plugin infrastructure
    /// </summary>
    [TestClass]
    public sealed class RedisProviderTests : CacheClientTestBase, IDisposable
    {
        /*
         * NOTE: This configuration object is serialized to mimic the 
         * server runtime loading this data in a json file with the 
         * same structure 
         */
        private static readonly object PluginConfig = new
        {
            debug = true,
            cache = new
            {
                // Test loading against the redis provider
                assembly_name           = "VNLib.Data.Caching.Providers.Redis.dll",
                url                     = TestConfig.TestRedisServerUrl,
                abort_on_connect_fail   = true,
                connect_timeout_ms      = 2000
            },

            secrets = new { }
        };

        private readonly TestPluginLoader<CacheProviderTestPlugin> _loader;
        private Task _onLoadTask;

        /// <inheritdoc/>
        protected override string KeyPrefix => "redis";

        public RedisProviderTests() : base()
        {
            _onLoadTask = Task.CompletedTask;

            string configData = JsonSerializer.Serialize(PluginConfig);

            // Load the test plugin with provider-specific configuration
            _loader = new TestPluginLoader<CacheProviderTestPlugin>()
                .WithCliArgs(["--verbose"])
                .WithHostConfig()
                .WithPluginConfigData(configData)
                .Load();
        }

        ///<inheritdoc/>
        protected override ICacheClient ConfigureClient()
        {
            ICacheClient cache = null!;

            _loader.GetServices(services =>
             {
                 // Extract the cache client from the plugin's exported services
                 Assert.IsTrue(services.HasService<ICacheClient>(), "ICacheClient should be exported by the plugin");
                 cache = services.GetService<ICacheClient>();                
             });

            // Get the init function to wait for the client to load
            // redis should always export the InitAsync function
            Func<Task>? initAsync = ManagedLibrary.TryGetMethod<Func<Task>>(cache, "InitAsync");

            Assert.IsNotNull(initAsync, "InitAsync method should be exported by the plugin");

            _onLoadTask = initAsync.Invoke();

            return cache;
        }

        ///<inheritdoc/>
        protected override Task EnsureLoadedAsync() => _onLoadTask;

        public void Dispose()
        {
            // Unload and dispose the plugin
            _loader?.Unload(delayMilliseconds: 1000)
                   .TryDispose();

            GC.SuppressFinalize(this);
        }
    }
}
