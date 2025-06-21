using Microsoft.VisualStudio.TestTools.UnitTesting;

using System;
using System.IO;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

using VNLib.Data.Caching.Exceptions;
using VNLib.Data.Caching.Providers.VNCache;
using VNLib.Utils.Logging;

namespace VNLib.Data.Caching.IntegrationTests
{
    [TestClass]
    public class VNCacheRemoteClientTest : VNCacheClientTestBase
    {
        private static string ServerKeyPath => Environment.GetEnvironmentVariable("TEST_VNCACHE_SERVER_KEY_PATH")!;

        private static string ClientKeyPath => Environment.GetEnvironmentVariable("TEST_VNCACHE_CLIENT_KEY_PATH")!;

        private static string TestServerUrl => Environment.GetEnvironmentVariable("TEST_VNCACHE_SERVER_URL")!;

        protected internal static VNRemoteCacheConfig GetRemoteConfig(ILogProvider logger)
        {
            byte[] confFileBytes = File.ReadAllBytes("client-config.json");
            byte[] sigKey = File.ReadAllBytes(ClientKeyPath);
            byte[] verifKey = File.ReadAllBytes(ServerKeyPath);

            VNRemoteCacheConfig clientConfig = JsonSerializer.Deserialize<VNRemoteCacheConfig>(confFileBytes)!;
            clientConfig.AuthManager = JwkAuthManager.FromJwkBytes(sigKey, verifKey);
            clientConfig.ClientDebugLog = logger;
            clientConfig.IsDebug = true;
            
            // Assign default serializers
            clientConfig.CacheObjectSerializer = new JsonCacheObjectSerializer(256);
            clientConfig.CacheObjectDeserializer = new JsonCacheObjectSerializer(256);

            //Always connect local test server
            clientConfig.InitialNodes = [TestServerUrl];

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

            await Assert.ThrowsExactlyAsync<InvalidStatusException>(() =>
                Client.Cache.AddOrUpdateAsync(key, null, RandomTestValue, CancellationToken.None)
            );

            await Assert.ThrowsExactlyAsync<InvalidStatusException>(() =>
                Client.Cache.GetAsync<string>(key, CancellationToken.None)
            );

            await Assert.ThrowsExactlyAsync<InvalidStatusException>(() =>
                Client.Cache.DeleteAsync(key, CancellationToken.None)
            );
        }

        protected override VNCacheClientHandle CreateClient(ILogProvider logger)
        {
            return VNCacheClient.CreateRemoteCache(GetRemoteConfig(logger));
        }
    }

}