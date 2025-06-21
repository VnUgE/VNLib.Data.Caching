using Microsoft.VisualStudio.TestTools.UnitTesting;

using Serilog;
using Serilog.Core;
using Serilog.Events;

using System;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

using VNLib.Utils.Logging;
using VNLib.Data.Caching.Providers.VNCache;

namespace VNLib.Data.Caching.IntegrationTests
{
    public abstract class VNCacheClientTestBase : IAsyncDisposable
    {
        private readonly SerilogLogger _logger;
        private readonly VNCacheClientHandle _client;
        private readonly Task _runTask;

        protected VNCacheClientHandle Client => _client;

        public VNCacheClientTestBase()
        {
            LoggerConfiguration lc = new();
            lc.WriteTo.Console();
            lc.MinimumLevel.Verbose();

            _logger = new SerilogLogger(lc.CreateLogger());

            //Debugger.Launch();

            _client = CreateClient(_logger);         

            //Start the client connection
            _runTask = _client.RunAsync(_logger);
        }

        protected abstract VNCacheClientHandle CreateClient(ILogProvider logger);

        [TestInitialize]
        public async Task InitializeAsync()
        {
            if (!_client.Cache.IsConnected)
            {
                _logger.Verbose("Waiting for client to connect");
                await Task.Delay(5000);

                for (int checks = 0; checks < 10; checks++)
                {
                    if (_client.Cache.IsConnected)
                    {
                        _logger.Verbose("Client connected");
                        break;
                    }

                    _logger.Verbose("Client not connected, waiting 2 seconds");
                    await Task.Delay(2000);
                }

                Assert.IsTrue(_client.Cache.IsConnected, "Client did not connect after 20 seconds");
            }
        }

        public async ValueTask DisposeAsync()
        {
            //Stop the client
            _client.StopListening();

            await _runTask.WaitAsync(TimeSpan.FromSeconds(1));

            Assert.IsFalse(_client.Cache.IsConnected, "Client did not stop listening");

            _client.StopListening();

            GC.SuppressFinalize(this);
        }

        [TestMethod]
        public async Task TestAddOrUpdateAsync()
        {
            const string key = "test-key";

            Assert.IsTrue(_client.Cache.IsConnected);

            //A random value must be used to ensure the test is not dependent on the server state
            string RandomTestValue = Guid.NewGuid().ToString();
            string RandomTestValue2 = Guid.NewGuid().ToString();

            // Add the value to the cache and check it is there
            await _client.Cache.AddOrUpdateAsync(key, null, RandomTestValue, CancellationToken.None);
            string? value = await _client.Cache.GetAsync<string>(key, CancellationToken.None);
            Assert.AreEqual(RandomTestValue, value);

            // Update the value in the cache and check the updated value is returned
            await _client.Cache.AddOrUpdateAsync(key, null, RandomTestValue2, CancellationToken.None);
            value = await _client.Cache.GetAsync<string>(key, CancellationToken.None);
            Assert.AreEqual(RandomTestValue2, value);

            // Remove the value from the cache and check it is no longer there
            await _client.Cache.DeleteAsync(key, CancellationToken.None);
            value = await _client.Cache.GetAsync<string>(key, CancellationToken.None);
            Assert.IsNull(value);
        }

        [TestMethod]
        public async Task TestUpdateKeyAsync()
        {
            const string key = "test-key-1";
            const string secondKey = "test-key-2";

            Assert.IsTrue(_client.Cache.IsConnected);

            //A random value must be used to ensure the test is not dependent on the server state
            string RandomTestValue = Guid.NewGuid().ToString();

            // Ensure the value is not in the cache
            string? value = await _client.Cache.GetAsync<string>(key, CancellationToken.None);
            Assert.IsNull(value);

            // Add the value to the cache and check it is there
            await _client.Cache.AddOrUpdateAsync(key, newKey: null, RandomTestValue, CancellationToken.None);
            value = await _client.Cache.GetAsync<string>(key, CancellationToken.None);

            Assert.AreEqual(RandomTestValue, value);

            // Update the key in the cache and check the updated value is returned
            await _client.Cache.AddOrUpdateAsync(key, secondKey, RandomTestValue, CancellationToken.None);
            value = await _client.Cache.GetAsync<string>(key, CancellationToken.None);
            // Ensure the value is no longer retrievable from the old key
            Assert.IsNull(value);

            value = await _client.Cache.GetAsync<string>(secondKey, CancellationToken.None);
            Assert.AreEqual(RandomTestValue, value);

            //Test that deleting the original key fails with false
            bool deleted = await _client.Cache.DeleteAsync(key, CancellationToken.None);
            Assert.IsFalse(deleted);

            // Remove the value from the cache and check it is no longer there
            await _client.Cache.DeleteAsync(secondKey, CancellationToken.None);
            value = await _client.Cache.GetAsync<string>(secondKey, CancellationToken.None);
            Assert.IsNull(value);

            //Ensure deleting the key again returns false
            deleted = await _client.Cache.DeleteAsync(secondKey, CancellationToken.None);
            Assert.IsFalse(deleted);
        }

        [TestMethod]
        public async Task TestGetAsync()
        {
            const string key = "test-key-3";

            Assert.IsTrue(_client.Cache.IsConnected);

            //A random value must be used to ensure the test is not dependent on the server state
            string RandomTestValue = Guid.NewGuid().ToString();
            byte[] randAsBytes = Encoding.UTF8.GetBytes(RandomTestValue);

            // Add the value to the cache and check it is there
            await _client.Cache.AddOrUpdateAsync(key, null, RandomTestValue, CancellationToken.None);
            string? value = await _client.Cache.GetAsync<string>(key, CancellationToken.None);
            Assert.AreEqual(RandomTestValue, value);

            //Store the value in raw bytes
            await _client.Cache.AddOrUpdateAsync(key, null, randAsBytes, CancellationToken.None);

            byte[]? readBytes = await _client.Cache.GetAsync<byte[], string?>(key, static (_, d) => d.ToArray(), state: null, CancellationToken.None);
            //Check the bytes are the same
            Assert.IsTrue(randAsBytes.AsSpan().SequenceEqual(readBytes));

            // Remove the value from the cache and check it is no longer there
            bool deleted = await _client.Cache.DeleteAsync(key, CancellationToken.None);
            Assert.IsTrue(deleted);

            value = await _client.Cache.GetAsync<string>(key, CancellationToken.None);
            Assert.IsNull(value);
        }

        [TestMethod]
        public abstract Task TestKeyTooSmallAsync();

        [TestMethod]
        public async Task TestMinKeySizeAsync()
        {
            // 4 chars should be okay although not ideal
            const string key = "abcd";

            //A random value must be used to ensure the test is not dependent on the server state
            string RandomTestValue = Guid.NewGuid().ToString();

            await _client.Cache.AddOrUpdateAsync(key, null, RandomTestValue, CancellationToken.None);

            await _client.Cache.GetAsync<string>(key, CancellationToken.None);

            await _client.Cache.DeleteAsync(key, CancellationToken.None);
        }

        [TestMethod]
        public async Task TestDeleteAsync()
        {
            const string key = "test-key-delete";

            Assert.IsTrue(_client.Cache.IsConnected);

            //A random value must be used to ensure the test is not dependent on the server state
            string RandomTestValue = Guid.NewGuid().ToString();

            // Add the value to the cache and check it is there
            await _client.Cache.AddOrUpdateAsync(key, null, RandomTestValue, CancellationToken.None);
            string? value = await _client.Cache.GetAsync<string>(key, CancellationToken.None);
            Assert.AreEqual(RandomTestValue, value);

            // Delete the value from the cache and check it is no longer there
            bool deleted = await _client.Cache.DeleteAsync(key, CancellationToken.None);
            Assert.IsTrue(deleted);

            value = await _client.Cache.GetAsync<string>(key, CancellationToken.None);
            Assert.IsNull(value);
        }
      
        [TestMethod]
        public async Task TestDefaultSerializerExtensions()
        {
            const string key = "test-key-extensions";

            Assert.IsTrue(_client.Cache.IsConnected);

            //A random value must be used to ensure the test is not dependent on the server state
            string RandomTestValue = Guid.NewGuid().ToString();
            string RandomTestValue2 = Guid.NewGuid().ToString();

            // Add the value to the cache and check it is there
            await _client.Cache.AddOrUpdateAsync(key, null, RandomTestValue, CancellationToken.None);
            string? value = await _client.Cache.GetAsync<string>(key, CancellationToken.None);
            Assert.AreEqual(RandomTestValue, value);

            // Update the value in the cache and check the updated value is returned
            await _client.Cache.AddOrUpdateAsync(key, null, RandomTestValue2, CancellationToken.None);
            value = await _client.Cache.GetAsync<string>(key, CancellationToken.None);
            Assert.AreEqual(RandomTestValue2, value);

            // Remove the value from the cache and check it is no longer there
            await _client.Cache.DeleteAsync(key, CancellationToken.None);
            value = await _client.Cache.GetAsync<string>(key, CancellationToken.None);
            Assert.IsNull(value);
        }

        [TestMethod]
        public async Task TestMemoryOverloadExtensions()
        {
            const string key = "test-key-extensions-memory";

            Assert.IsTrue(_client.Cache.IsConnected);

            // Test with ReadOnlyMemory<byte>
            ReadOnlyMemory<byte> testData = Guid.NewGuid().ToByteArray();            
            await _client.Cache.AddOrUpdateAsync(key, null, testData, CancellationToken.None);

            byte[]? retrievedData = await _client.Cache.GetAsync<byte[], string?>(key, static (_, d) => d.ToArray(), state: null, CancellationToken.None);
            Assert.IsTrue(testData.Span.SequenceEqual(retrievedData));

            // Test with Memory<byte>
            Memory<byte> testData2 = Encoding.UTF8.GetBytes(Guid.NewGuid().ToString());
            await _client.Cache.AddOrUpdateAsync(key, null, testData2, CancellationToken.None);

            byte[]? retrievedData2 = await _client.Cache.GetAsync<byte[], string?>(key, static (_, d) => d.ToArray(), state: null, CancellationToken.None);
            Assert.IsTrue(testData2.Span.SequenceEqual(retrievedData2));

            // Test with byte[]
            byte[] testData3 = Encoding.UTF8.GetBytes(Guid.NewGuid().ToString());
            await _client.Cache.AddOrUpdateAsync(key, null, testData3, CancellationToken.None);

            byte[]? retrievedData3 = await _client.Cache.GetAsync<byte[], string?>(key, static (_, d) => d.ToArray(), state: null, CancellationToken.None);
            Assert.IsTrue(testData3.AsSpan().SequenceEqual(retrievedData3));

            await _client.Cache.DeleteAsync(key, CancellationToken.None);
        }

        [TestMethod]
        public async Task TestObjectDataOverloads()
        {
            const string key = "test-key-extensions-objectdata";

            Assert.IsTrue(_client.Cache.IsConnected);

            // Create a test object data implementation
            byte[] randomData = Guid.NewGuid().ToByteArray();
            TestObjectData testData = new(randomData);

            // Test AddOrUpdateAsync with IObjectData
            await _client.Cache.AddOrUpdateAsync(key, null, (IObjectData)testData, CancellationToken.None);

            // Test GetAsync with IObjectData
            TestObjectData retrieveData = new();
            await _client.Cache.GetAsync(key, retrieveData, CancellationToken.None);

            Assert.IsTrue(randomData.AsSpan().SequenceEqual(retrieveData.Data));

            bool deleted = await _client.Cache.DeleteAsync(key, CancellationToken.None);
            Assert.IsTrue(deleted);
        }

        private sealed class TestObjectData : IObjectData
        {
            public byte[] Data { get; private set; }

            public TestObjectData() => Data = [];

            public TestObjectData(byte[] data) => Data = data;

            ///<inheritdoc/>
            public ReadOnlySpan<byte> GetData() 
                => Data;

            ///<inheritdoc/>
            public void SetData(ReadOnlySpan<byte> data) 
                => Data = data.ToArray();
        }

        private sealed class SerilogLogger(Logger log) : ILogProvider
        {
            public void Flush()
            { }

            public object GetLogProvider() => log;

            public bool IsEnabled(LogLevel level) => true;

            public void Write(LogLevel level, string value)
                => log.Write((LogEventLevel)level, value);

            public void Write(LogLevel level, Exception exception, string value = "")
                => log.Write((LogEventLevel)level, exception, value);

            public void Write(LogLevel level, string value, params object?[] args)
                => log.Write((LogEventLevel)level, value, args);

            public void Write(LogLevel level, string value, params ValueType[] args)
                => log.Write((LogEventLevel)level, value, args);
        }
    }
}
