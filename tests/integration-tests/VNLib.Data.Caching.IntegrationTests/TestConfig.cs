/*
* Copyright (c) 2026 Vaughn Nugent
*
* Library: VNLib
* Package: VNLib.Data.Caching.IntegrationTests
* File: TestConfigExtensions.cs 
*
* TestConfigExtensions.cs is part of VNLib.Data.Caching.IntegrationTests which is part of 
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

using VNLib.Plugins;
using VNLib.Plugins.Essentials.ServiceStack.Testing;

namespace VNLib.Data.Caching.IntegrationTests
{
    /// <summary>
    /// Provides extension methods for configuring the <see cref="TestPluginLoader{T}"/> 
    /// with JSON configuration files from the Config directory.
    /// </summary>
    internal static class TestConfig
    {

        public static string ServerKeyPath => Environment.GetEnvironmentVariable("TEST_VNCACHE_SERVER_KEY_PATH")!;

        public static string ClientKeyPath => Environment.GetEnvironmentVariable("TEST_VNCACHE_CLIENT_KEY_PATH")!;

        public static string TestVncacheServerUrl => Environment.GetEnvironmentVariable("TEST_VNCACHE_SERVER_URL")!;

        public static string TestRedisServerUrl => Environment.GetEnvironmentVariable("TEST_REDIS_SERVER_URL")!;

        private static readonly object HostConfig = new
        {
             // Create a basic host-level plugins object
            plugins = new {

                enabled         = true,
                hot_reload      = false,
                paths           = Array.Empty<string>(),

                // Assets are spit out into an assets directory by msbuild during build
                // See project file for why/where this directory is created
                assets          = "assets/",
            }
        };


        /// <summary>
        /// Loads the shared host config file from the Config directory or TEST_CONFIG_DIR environment variable
        /// </summary>
        /// <typeparam name="T">The plugin type</typeparam>
        /// <param name="loader">The test plugin loader instance</param>
        /// <returns>The current loader instance for chaining</returns>
        public static TestPluginLoader<T> WithHostConfig<T>(this TestPluginLoader<T> loader) where T : class, IPlugin, new()
        {
            byte[] configData = JsonSerializer.SerializeToUtf8Bytes(HostConfig);
            
            return loader.WithHostConfigData(configData);
        }       
    }
}
