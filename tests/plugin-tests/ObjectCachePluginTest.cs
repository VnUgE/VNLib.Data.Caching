/*
* Copyright (c) 2026 Vaughn Nugent
* 
* Library: VNLib
* Package: VNLib.Data.Caching.Tests
* File: ObjectCachePluginTest.cs 
*
* ObjectCachePluginTest.cs is part of VNLib.Data.Caching.Tests which is part of the larger 
* VNLib collection of libraries and utilities.
*
* VNLib.Data.Caching.Tests is free software: you can redistribute it and/or modify 
* it under the terms of the GNU Affero General Public License as 
* published by the Free Software Foundation, either version 3 of the
* License, or (at your option) any later version.
*
* VNLib.Data.Caching.Tests is distributed in the hope that it will be useful,
* but WITHOUT ANY WARRANTY; without even the implied warranty of
* MERCHANTABILITY or FITNESS FOR A PARTICULAR PURPOSE. See the
* GNU Affero General Public License for more details.
*
* You should have received a copy of the GNU Affero General Public License
* along with this program.  If not, see https://www.gnu.org/licenses/.
*/

using System;

using Microsoft.VisualStudio.TestTools.UnitTesting;

using VNLib.Data.Caching.ObjectCache.Server;
using VNLib.Plugins.Essentials.Runtime;
using VNLib.Plugins.Essentials.ServiceStack.Testing;

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
                   Assert.HasCount(3, services.GetEndpoints());
               })
               .Unload(delayMilliseconds: 3500)
               .TryDispose();
        }
    }
}
