using Microsoft.VisualStudio.TestTools.UnitTesting;

using System;
using System.IO;
using System.Text.Json;

using VNLib.Utils.Logging;
using VNLib.Data.Caching.Providers.VNCache;

namespace VNLib.Data.Caching.IntegrationTests
{
    [TestClass]
    public class VNCacheMemoryTests: VNCacheClientTestBase
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

        protected override VNCacheClientHandle CreateClient(ILogProvider logger)
        {
            return VNCacheClient.CreateMemoryCache(GetMemoryConfig());
        }
    }

}