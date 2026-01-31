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

using System;
using System.IO;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

using Microsoft.VisualStudio.TestTools.UnitTesting;

using VNLib.Data.Caching.Providers.VNCache;
using VNLib.Utils.Logging;

namespace VNLib.Data.Caching.IntegrationTests
{
    [TestClass]
    public class VNCacheMemoryTests : VNCacheClientTestBase
    {
        protected internal static VNMemoryCacheConfig GetMemoryConfig()
        {
            byte[] confFileBytes = File.ReadAllBytes("client-config.json");

            VNMemoryCacheConfig clientConfig = JsonSerializer.Deserialize<VNMemoryCacheConfig>(confFileBytes)!;

            clientConfig.IsDebug = true;
            clientConfig.CacheObjectSerializer = new JsonCacheObjectSerializer(256);
            clientConfig.CacheObjectDeserializer = new JsonCacheObjectSerializer(256);

            string asString = JsonSerializer.Serialize(clientConfig, new JsonSerializerOptions { WriteIndented = true });
            Console.WriteLine($"Client config:\n{asString}");

            return clientConfig;
        }
     
        public override async Task TestKeyTooSmallAsync()
        {
            //A key less than about 4 charters is not allowed
            const string key = "a";

            Assert.IsTrue(Client.Cache.IsConnected);

            //A random value must be used to ensure the test is not dependent on the server state
            string RandomTestValue = Guid.NewGuid().ToString();

            await Assert.ThrowsExactlyAsync<ArgumentOutOfRangeException>(() =>
                Client.Cache.AddOrUpdateAsync(key, null, RandomTestValue, CancellationToken.None)
            );

            await Assert.ThrowsExactlyAsync<ArgumentOutOfRangeException>(() =>
                Client.Cache.GetAsync<string>(key, CancellationToken.None)
            );

            await Assert.ThrowsExactlyAsync<ArgumentOutOfRangeException>(() =>
                Client.Cache.DeleteAsync(key, CancellationToken.None)
            );        
        }

        protected override VNCacheClientHandle CreateClient(ILogProvider logger)
        {
            return VNCacheClient.CreateMemoryCache(GetMemoryConfig());
        }
    }

}