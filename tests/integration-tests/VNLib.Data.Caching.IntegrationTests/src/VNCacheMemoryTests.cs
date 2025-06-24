using Microsoft.VisualStudio.TestTools.UnitTesting;

using System;
using System.IO;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

using VNLib.Utils.Logging;
using VNLib.Data.Caching.Providers.VNCache;

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