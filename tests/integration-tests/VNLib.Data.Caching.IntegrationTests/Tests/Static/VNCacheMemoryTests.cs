/*
* Copyright (c) 2026 Vaughn Nugent
* 
* Library: VNLib
* Package: VNLib.Data.Caching.IntegrationTests
* File: VNCacheMemoryTests.cs 
*
* VNCacheMemoryTests.cs is part of VNLib.Data.Caching.IntegrationTests which is part of the larger 
* VNLib collection of libraries and utilities.
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
using System.Threading;
using System.Threading.Tasks;

using VNLib.Data.Caching.Providers.VNCache;

namespace VNLib.Data.Caching.IntegrationTests.Tests.Static
{
    /// <summary>
    /// Integration tests for VNCache memory-only provider (direct API)
    /// </summary>
    [TestClass]
    public class VNCacheMemoryTests : CacheClientTestBase, IDisposable
    {
        /*
         * NOTE: Configuration matches expected config objects pulled
         * from json file. 
         * 
         * Most defaults should be fine for prod so they're commented 
         * out during testing
         */

        private static readonly object MemoryConfig = new
        {
            is_debug                    = true,
            // max_object_size             = 8192,
            // max_age_sec                 = 600,
            // zero_all                    = false,
            // buckets                     = 10,
            // bucket_size                 = 500,
            // use_private_buffer_heap     = false
        };

        private readonly VNCacheClientHandle _clientHandle;
        private readonly Task _runTask;
        private readonly Task _loadTask;

        ///<inheritdoc/>
        protected override string KeyPrefix => "memory";

        public VNCacheMemoryTests() : base()
        {
            byte[] configBytes = JsonSerializer.SerializeToUtf8Bytes(MemoryConfig);
            VNMemoryCacheConfig clientConfig = JsonSerializer.Deserialize<VNMemoryCacheConfig>(configBytes)!;
            
            clientConfig.CacheObjectSerializer = new JsonCacheObjectSerializer(256);
            clientConfig.CacheObjectDeserializer = new JsonCacheObjectSerializer(256);

            string asString = JsonSerializer.Serialize(clientConfig, new JsonSerializerOptions { WriteIndented = true });
            Console.WriteLine($"Client config:\n{asString}");

            _clientHandle = VNCacheClient.CreateMemoryCache(clientConfig);
            _runTask = _clientHandle.RunAsync(new SerilogLogger());

            _loadTask = Task.Delay(100);
        }

        ///<inheritdoc/>
        protected override ICacheClient ConfigureClient() => _clientHandle.Cache;

        ///<inheritdoc/>
        protected override Task EnsureLoadedAsync()
        {
            // Wait for connection with polling
            Assert.IsTrue(_clientHandle.Cache.IsConnected, "Memory cache should always be connected after startup");

            return _loadTask;
        }

        [TestMethod]
        public async Task TestKeyTooSmallAsync()
        {
            //A key less than about 4 characters is not allowed
            const string key = "a";

            await EnsureLoadedAsync();         

            //A random value must be used to ensure the test is not dependent on the server state
            string RandomTestValue = Guid.NewGuid().ToString();

            await Assert.ThrowsExactlyAsync<ArgumentOutOfRangeException>(() =>
                Client.AddOrUpdateAsync(key, null, RandomTestValue, CancellationToken.None)
            );

            await Assert.ThrowsExactlyAsync<ArgumentOutOfRangeException>(() =>
                Client.GetAsync<string>(key, CancellationToken.None)
            );

            await Assert.ThrowsExactlyAsync<ArgumentOutOfRangeException>(() =>
                Client.DeleteAsync(key, CancellationToken.None)
            );        
        }

        public void Dispose()
        {
            _clientHandle.StopListening();

            _runTask.Wait(TimeSpan.FromSeconds(1));

            GC.SuppressFinalize(this);
        }
    }
}