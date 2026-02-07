/*
* Copyright (c) 2026 Vaughn Nugent
*
* Library: VNLib
* Package: VNLib.Data.Caching.IntegrationTests
* File: VNCacheProviderTests.cs 
*
* VNCacheProviderTests.cs is part of VNLib.Data.Caching.IntegrationTests which is part of 
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

using Microsoft.VisualStudio.TestTools.UnitTesting;

using System;
using System.Text.Json;
using System.Threading.Tasks;

using VNLib.Utils.Resources;
using VNLib.Plugins.Essentials.ServiceStack.Testing;

using VNLib.Data.Caching.IntegrationTests.TestPlugins;


namespace VNLib.Data.Caching.IntegrationTests.Tests.Dynamic
{
    /// <summary>
    /// Integration tests for the VNCache provider loaded via plugin infrastructure
    /// </summary>
    [TestClass]
    public sealed class VNCacheProviderTests : CacheClientTestBase, IDisposable
    {
        /*
        * NOTE: This configuration object is serialized to mimic the 
        * server runtime loading this data in a json file with the 
        * same structure 
        */
        static readonly object PluginConfig = new
        {
            debug = true,
            cache = new
            {
                assembly_name = "VNLib.Data.Caching.Providers.VNCache.dll",

                initial_nodes = new [] { TestConfig.TestVncacheServerUrl },

                use_tls                 = false,
                memory_only             = false,
                max_object_size         = 8128,
                request_timeout_sec     = 2,
                discovery_interval_sec  = 1,

                // Disable memory caching for now
                // memory_cache = new { }               
            },

            secrets = new
            {
                // Secret keys should be read from files
                // also tests the secret loading system :)
                client_private_key  = $"file://{TestConfig.ClientKeyPath}",
                cache_public_key    = $"file://{TestConfig.ServerKeyPath}"
            }
        };

        /// <inheritdoc/>
        protected override string KeyPrefix => "vncache";

        private readonly TestPluginLoader<CacheProviderTestPlugin> _loader;
        private Task _onLoadTask;       

        public VNCacheProviderTests(): base()
        {
            _onLoadTask = Task.CompletedTask;

            //Get config binary data from test config
            byte[] pluginConfig = JsonSerializer.SerializeToUtf8Bytes(PluginConfig);

            // Load the test plugin with provider-specific configuration
            _loader = new TestPluginLoader<CacheProviderTestPlugin>()
                .WithCliArgs(["--verbose"])
                .WithHostConfig()
                .WithPluginConfigData(pluginConfig)
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
            Func<Task>? initAsync = ManagedLibrary.TryGetMethod<Func<Task>>(cache, "InitAsync");
            _onLoadTask = initAsync?.Invoke() ?? Task.CompletedTask;

            return cache;
        }

        ///<inheritdoc/>
        protected override async Task EnsureLoadedAsync()
        {
            await _onLoadTask;

            if (!Client.IsConnected)
            {
                await Task.Delay(5000);

                for (int checks = 0; checks < 10; checks++)
                {
                    if (Client.IsConnected)
                    {
                        break;
                    }

                    await Task.Delay(2000);
                }

                Assert.IsTrue(Client.IsConnected, "Client did not connect after 20 seconds");
            }
        }

        public void Dispose()
        {
            // Unload and dispose the plugin
            _loader?.Unload(delayMilliseconds: 1000)
                   .TryDispose();

            GC.SuppressFinalize(this);
        }
    }
}
