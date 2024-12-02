using Microsoft.VisualStudio.TestTools.UnitTesting;

using System;

using VNLib.Plugins.Essentials.Runtime;
using VNLib.Plugins.Essentials.ServiceStack.Testing;

using VNLib.Data.Caching.ObjectCache.Server;

namespace VNLib.Data.Caching.Tests
{
    [TestClass()]
    public class ObjectCachePluginTest
    {
        const string TestPluginConfigPath = "../../../../../plugins/ObjectCacheServer/src/ObjectCacheServer.sample.json";

        private static string HostConfigFilePath => Environment.GetEnvironmentVariable("TEST_HOST_CONFIG_FILE")!;

        [TestMethod()]
        public void TestObjectCachePlugin()
        {
            new TestPluginLoader<ObjectCacheServerEntry>()
               .WithCliArgs(["--verbose"])  //Enable verbose logging and account setup mode
               .WithHostConfigFile(HostConfigFilePath)
               .WithPluginConfigFile(TestPluginConfigPath)
               .Load()
               .GetServices(services =>
               {
                   //Onlly an endpoint provider should be exported
                   Assert.AreEqual(1, services.Count);
                   Assert.IsTrue(services.HasService<IVirtualEndpointDefinition>());

                   //Currently 3 endpoints are exported
                   Assert.AreEqual(3, services.GetEndpoints().Length);
               })
               .Unload(delayMilliseconds: 3500)
               .TryDispose();
        }
    }
}
