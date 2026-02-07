/*
* Copyright (c) 2026 Vaughn Nugent
* 
* Library: VNLib
* Package: VNLib.Data.Caching.IntegrationTests
* File: VNCacheRemoteClientTest.cs 
*
* VNCacheRemoteClientTest.cs is part of VNLib.Data.Caching.IntegrationTests which is part of the larger 
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

using Serilog;
using Serilog.Core;
using Serilog.Events;

using System;
using System.IO;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

using VNLib.Utils.Logging;
using VNLib.Data.Caching.Exceptions;
using VNLib.Data.Caching.Providers.VNCache;

namespace VNLib.Data.Caching.IntegrationTests.Tests.Static
{
    /// <summary>
    /// Integration tests for VNCache remote client (direct API)
    /// </summary>
    [TestClass]
    public class VNCacheRemoteClientTest : CacheClientTestBase, IDisposable
    {
        private static readonly object PluginConfig = new
        {
            is_debug                = true,
            use_tls                 = false,          

            max_object_size             = 8192,
            // max_age_sec                 = 600,
            // zero_all                    = false,                   
            
            intial_delay_ms         = 200,  // Lower delay time for testing
            discovery_interval_sec  = 1000, 
            request_timeout_sec     = 2,    // Lower timeout for testing  

            // Connect to test server
            initial_nodes           = new [] { TestConfig.TestVncacheServerUrl }
        };
       
        private readonly VNCacheClientHandle _clientHandle;
        private readonly Task _runTask;

        ///<inheritdoc/>
        protected override string KeyPrefix => "remote";

        public VNCacheRemoteClientTest() : base()
        {
            byte[] configBytes = JsonSerializer.SerializeToUtf8Bytes(PluginConfig);
            byte[] sigKey = File.ReadAllBytes(TestConfig.ClientKeyPath);
            byte[] verifKey = File.ReadAllBytes(TestConfig.ServerKeyPath);

            VNRemoteCacheConfig clientConfig = JsonSerializer.Deserialize<VNRemoteCacheConfig>(configBytes)!;
            clientConfig.AuthManager = JwkAuthManager.FromJwkBytes(sigKey, verifKey);           

            // Assign default serializers
            clientConfig.CacheObjectSerializer = new JsonCacheObjectSerializer(256);
            clientConfig.CacheObjectDeserializer = new JsonCacheObjectSerializer(256);
     
            Console.WriteLine(
                "Client config:\n{0}", 
                JsonSerializer.Serialize(clientConfig, new JsonSerializerOptions { WriteIndented = true })
            );

            _clientHandle = VNCacheClient.CreateRemoteCache(clientConfig);           

            _runTask = _clientHandle.RunAsync(new SerilogLogger());
        }

        protected override ICacheClient ConfigureClient() => _clientHandle.Cache;

        protected override async Task EnsureLoadedAsync()
        {
            // Wait for connection with polling
            if (!Client.IsConnected)
            {
                await Task.Delay(5000);

                for (int checks = 0; checks < 10; checks++)
                {
                    if (_clientHandle.Cache.IsConnected)
                    {
                        break;
                    }

                    await Task.Delay(2000);
                }

                Assert.IsTrue(_clientHandle.Cache.IsConnected, "Client did not connect after 20 seconds");
            }
        }
       

        [TestMethod]
        public async Task TestKeyTooSmallAsync()
        {
            //A key less than about 4 charters is not allowed
            const string key = "a";

            await EnsureLoadedAsync();

            //A random value must be used to ensure the test is not dependent on the server state
            string RandomTestValue = Guid.NewGuid().ToString();          

            await Assert.ThrowsExactlyAsync<InvalidStatusException>(() =>
                Client.AddOrUpdateAsync(key, null, RandomTestValue, CancellationToken.None)
            );

            await Assert.ThrowsExactlyAsync<InvalidStatusException>(() =>
                Client.GetAsync<string>(key, CancellationToken.None)
            );

            await Assert.ThrowsExactlyAsync<InvalidStatusException>(() =>
                Client.DeleteAsync(key, CancellationToken.None)
            );
        }

        public void Dispose()
        {
            _clientHandle.StopListening();

            _runTask.Wait(TimeSpan.FromSeconds(1));

            GC.SuppressFinalize(this);
        }

        internal sealed class SerilogLogger : ILogProvider
        {
            private readonly Logger _log;

            public SerilogLogger()
            {
                LoggerConfiguration lc = new();
                
                lc.WriteTo.Console();
                lc.MinimumLevel.Verbose();

                _log = lc.CreateLogger();
            }

            public void Flush()
            { }

            public object GetLogProvider() => _log;

            public bool IsEnabled(LogLevel level) => true;

            public void Write(LogLevel level, string value)
                => _log.Write((LogEventLevel)level, value);

            public void Write(LogLevel level, Exception exception, string value = "")
                => _log.Write((LogEventLevel)level, exception, value);

            public void Write(LogLevel level, string value, params object?[] args)
                => _log.Write((LogEventLevel)level, value, args);

            public void Write(LogLevel level, string value, params ValueType[] args)
                => _log.Write((LogEventLevel)level, value, args);
        }
    }
}