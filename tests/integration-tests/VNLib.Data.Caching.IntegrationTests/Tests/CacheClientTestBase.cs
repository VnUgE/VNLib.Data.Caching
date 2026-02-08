/*
* Copyright (c) 2026 Vaughn Nugent
*
* Library: VNLib
* Package: VNLib.Data.Caching.IntegrationTests
* File: CacheClientTestBase.cs 
*
* CacheClientTestBase.cs is part of VNLib.Data.Caching.IntegrationTests which is part of 
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
using System.Threading;
using System.Threading.Tasks;

using Microsoft.VisualStudio.TestTools.UnitTesting;

using VNLib.Hashing;
using VNLib.Utils.Resources;

namespace VNLib.Data.Caching.IntegrationTests.Tests
{
    /// <summary>
    /// Abstract base class for cache client integration tests.
    /// Provides common test methods that work against any ICacheClient implementation.
    /// </summary>
    public abstract class CacheClientTestBase
    {      

        private readonly LazyInitializer<ICacheClient> _client;

        /// <summary>
        /// Gets the configured cache client instance
        /// </summary>
        protected ICacheClient Client => _client.Instance;

        protected CacheClientTestBase()
        {
            _client = new(ConfigureClient);
        }

        /// <summary>
        /// Configures the cache client instance for use during testing
        /// </summary>
        /// <returns>The configured cache client</returns>
        protected abstract ICacheClient ConfigureClient();

        /// <summary>
        /// Ensures startup tasks have completed before using the cache client
        /// </summary>
        /// <returns>A task that completes when the client is ready to use</returns>
        protected abstract Task EnsureLoadedAsync();

        /// <summary>
        /// Gets the key prefix to use for test keys (to avoid collisions between providers)
        /// </summary>
        protected abstract string KeyPrefix { get; }

        [TestMethod]
        public async Task TestIsConnectedAsync()
        {
            Assert.IsNotNull(Client);
            await EnsureLoadedAsync();

            Assert.IsTrue(Client.IsConnected, $"{KeyPrefix} provider should be connected");
        }

        [TestMethod]
        public async Task TestAddOrUpdateAsync()
        {
            Assert.IsNotNull(Client);
            await EnsureLoadedAsync();

            string key = $"{KeyPrefix}-test-key-add-update";

            string? retrievedValue;

            // Add the initial value
            {
                // Generate random test values to ensure test independence
                string randomValue1 = Guid.NewGuid().ToString();

                // Add the value to the cache
                await Client.AddOrUpdateAsync(key, null, randomValue1, CancellationToken.None);
                retrievedValue = await Client.GetAsync<string>(key, CancellationToken.None);
                Assert.AreEqual(randomValue1, retrievedValue, "Retrieved value should match the added value");
            }

            // Update the value with the same key
            {
                string randomValue2 = Guid.NewGuid().ToString();

                // Update the value in the cache
                await Client.AddOrUpdateAsync(key, null, randomValue2, CancellationToken.None);

                retrievedValue = await Client.GetAsync<string>(key, CancellationToken.None);
                Assert.AreEqual(randomValue2, retrievedValue, "Retrieved value should match the updated value");
            }

            // Clean up
            {
                bool deleted = await Client.DeleteAsync(key, CancellationToken.None);
                Assert.IsTrue(deleted, "Delete should succeed");
            }
        }

        [TestMethod]
        public async Task TestDeleteAsync()
        {
            Assert.IsNotNull(Client);
            await EnsureLoadedAsync();

            string key = $"{KeyPrefix}-test-key-delete";

            string randomValue = Guid.NewGuid().ToString();
            string? retrievedValue;

            // Add a value
            {
                await Client.AddOrUpdateAsync(key, null, randomValue, CancellationToken.None);

                // Verify it exists
                retrievedValue = await Client.GetAsync<string>(key, CancellationToken.None);
                Assert.AreEqual(randomValue, retrievedValue, "Value should exist before deletion");
            }

            // Delete the value
            {
                bool deleted = await Client.DeleteAsync(key, CancellationToken.None);
                Assert.IsTrue(deleted, "Delete operation should return true");
            }

            // Verify deletion
            {
                retrievedValue = await Client.GetAsync<string>(key, CancellationToken.None);
                Assert.IsNull(retrievedValue, "Value should be null after deletion");
            }

            // Verify deleting the already-moved key returns false
            {               
                string secondKey = $"{KeyPrefix}-test-key-delete-new";

                bool deleted = await Client.DeleteAsync(key, CancellationToken.None);
                Assert.IsFalse(deleted, "Delete should return false for non-existent key");

                // Verify double-delete also returns false
                await Client.DeleteAsync(secondKey, CancellationToken.None);
                deleted = await Client.DeleteAsync(secondKey, CancellationToken.None);
                Assert.IsFalse(deleted, "Double-delete should return false");
            }
        }

        [TestMethod]
        public async Task TestGetNonExistentKeyAsync()
        {
            Assert.IsNotNull(Client);
            await EnsureLoadedAsync();

            string key = $"{KeyPrefix}-test-key-nonexistent";

            // Try to get a value that doesn't exist
            string? retrievedValue = await Client.GetAsync<string>(key, CancellationToken.None);
            Assert.IsNull(retrievedValue, "Non-existent key should return null");
        }

        [TestMethod]
        public async Task TestUpdateKeyAsync()
        {
            Assert.IsNotNull(Client);
            await EnsureLoadedAsync();          

            string oldKey = $"{KeyPrefix}-test-key-update-old";
            string newKey = $"{KeyPrefix}-test-key-update-new";

            string randomValue = Guid.NewGuid().ToString();

            string? retrievedValue;

            // Add a value with the old key
            {
                await Client.AddOrUpdateAsync(oldKey, null, randomValue, CancellationToken.None);

                retrievedValue = await Client.GetAsync<string>(oldKey, CancellationToken.None);
                Assert.AreEqual(randomValue, retrievedValue, "Value should exist at old key");
            }

            // Update with a new key and ensure old key is deleted          
            {
                await Client.AddOrUpdateAsync(oldKey, newKey, randomValue, CancellationToken.None);

                retrievedValue = await Client.GetAsync<string>(oldKey, CancellationToken.None);
                Assert.IsNull(retrievedValue, "Old key should be deleted");
            }

            // Verify new key has the correct value
            {
                retrievedValue = await Client.GetAsync<string>(newKey, CancellationToken.None);
                Assert.AreEqual(randomValue, retrievedValue, "Value should exist at new key");
            }

            // Clean up
            {

                bool deleted = await Client.DeleteAsync(newKey, CancellationToken.None);
                Assert.IsTrue(deleted, "Delete should succeed");
            }
           
        }

        [TestMethod]
        public async Task TestGetWithRawBytesAsync()
        {
            Assert.IsNotNull(Client);
            await EnsureLoadedAsync();

            string key = $"{KeyPrefix}-test-key-raw-bytes";          

            {
                byte[] randomBytes = RandomHash.GetRandomBytes();

                // Store raw bytes
                await Client.AddOrUpdateAsync(key, null, randomBytes, CancellationToken.None);

                // Retrieve as raw bytes using callback
                byte[]? retrievedBytes = await Client.GetAsync<byte[], string?>(
                    key,
                    static (_, data) => data.ToArray(),
                    state: null,
                    CancellationToken.None
                );

                Assert.IsNotNull(retrievedBytes, "Retrieved bytes should not be null");
                Assert.IsTrue(randomBytes.AsSpan().SequenceEqual(retrievedBytes), "Byte arrays should match");
            }

            // Clean up
            {

                bool deleted = await Client.DeleteAsync(key, CancellationToken.None);
                Assert.IsTrue(deleted, "Delete should succeed");
            }
        }

        [TestMethod]
        public async Task TestMinKeySizeAsync()
        {
            Assert.IsNotNull(Client);
            await EnsureLoadedAsync();

            // 4 characters should be acceptable (though not ideal)
            string key = $"{KeyPrefix}abcd";

            // Test adding and retrieving with minimum key size
            {
                string randomValue = Guid.NewGuid().ToString();

                await Client.AddOrUpdateAsync(key, null, randomValue, CancellationToken.None);
                string? retrievedValue = await Client.GetAsync<string>(key, CancellationToken.None);

                Assert.AreEqual(randomValue, retrievedValue, "Minimum key size should work");
            }

            // Clean up
            {
                bool deleted = await Client.DeleteAsync(key, CancellationToken.None);
                Assert.IsTrue(deleted, "Delete should succeed");
            }
        }

        [TestMethod]
        public async Task TestDefaultSerializerExtensionsAsync()
        {
            Assert.IsNotNull(Client);
            await EnsureLoadedAsync();

            string key = $"{KeyPrefix}-test-default-serializer";

            string? retrievedValue;          

            // Test using default serializer (no explicit serializer parameter)
            {
                string randomValue1 = Guid.NewGuid().ToString();

                await Client.AddOrUpdateAsync(key, null, randomValue1, CancellationToken.None);
                retrievedValue = await Client.GetAsync<string>(key, CancellationToken.None);

                Assert.AreEqual(randomValue1, retrievedValue, "Default serializer should work for add");
            }

            // Update using default serializer
            {
                string randomValue2 = Guid.NewGuid().ToString();
              
                await Client.AddOrUpdateAsync(key, null, randomValue2, CancellationToken.None);
                retrievedValue = await Client.GetAsync<string>(key, CancellationToken.None);

                Assert.AreEqual(randomValue2, retrievedValue, "Default serializer should work for update");
            }

            // Clean up
            {
                bool deleted = await Client.DeleteAsync(key, CancellationToken.None);
                Assert.IsTrue(deleted, "Delete should succeed");               
            }
        }

        [TestMethod]
        public async Task TestMemoryOverloadExtensionsAsync()
        {
            Assert.IsNotNull(Client);
            await EnsureLoadedAsync();

            string key = $"{KeyPrefix}-test-memory-overloads";

            // Test with ReadOnlyMemory<byte>
            {
                ReadOnlyMemory<byte> testData1 = RandomHash.GetRandomBytes();
                await Client.AddOrUpdateAsync(key, null, testData1, CancellationToken.None);

                byte[]? retrieved1 = await Client.GetAsync<byte[], string?>(
                    key,
                    static (_, data) => data.ToArray(),
                    state: null,
                    CancellationToken.None
                );

                Assert.IsNotNull(retrieved1, "ReadOnlyMemory test: Retrieved data should not be null");
                Assert.IsTrue(testData1.Span.SequenceEqual(retrieved1), "ReadOnlyMemory data should match");
            }

            // Test with Memory<byte>
            {
                Memory<byte> testData2 = RandomHash.GetRandomBytes();
                await Client.AddOrUpdateAsync(key, null, testData2, CancellationToken.None);

                byte[]? retrieved2 = await Client.GetAsync<byte[], string?>(
                    key,
                    static (_, data) => data.ToArray(),
                    state: null,
                    CancellationToken.None
                );

                Assert.IsNotNull(retrieved2, "Memory test: Retrieved data should not be null");
                Assert.IsTrue(testData2.Span.SequenceEqual(retrieved2), "Memory data should match");
            }

            // Test with byte[]
            {
                byte[] testData3 = RandomHash.GetRandomBytes();
                await Client.AddOrUpdateAsync(key, null, testData3, CancellationToken.None);

                byte[]? retrieved3 = await Client.GetAsync<byte[], string?>(
                    key,
                    static (_, data) => data.ToArray(),
                    state: null,
                    CancellationToken.None
                );

                Assert.IsNotNull(retrieved3, "Byte array test: Retrieved data should not be null");
                Assert.IsTrue(testData3.AsSpan().SequenceEqual(retrieved3), "Byte array data should match");
            }

            // Clean up
            {
                bool deleted = await Client.DeleteAsync(key, CancellationToken.None);
                Assert.IsTrue(deleted, "Delete should succeed");
            }
        }

        [TestMethod]
        public async Task TestObjectDataOverloadsAsync()
        {
            Assert.IsNotNull(Client);
            await EnsureLoadedAsync();

            string key = $"{KeyPrefix}-test-objectdata";

            // Create test data
            byte[] randomData = Guid.NewGuid().ToByteArray();

            {
                TestObjectData testData = new(randomData);

                // Test AddOrUpdateAsync with IObjectData
                await Client.AddOrUpdateAsync(key, null, (IObjectData)testData, CancellationToken.None);
            }

            {
                // Test GetAsync with IObjectData
                TestObjectData retrievedData = new();
                await Client.GetAsync(key, retrievedData, CancellationToken.None);

                Assert.IsNotNull(retrievedData.Data, "Retrieved data should not be null");
                Assert.IsTrue(randomData.AsSpan().SequenceEqual(retrievedData.Data), "IObjectData should match");
            }

            {
                // Clean up
                bool deleted = await Client.DeleteAsync(key, CancellationToken.None);
                Assert.IsTrue(deleted, "Delete should succeed");
            }
        }

        /// <summary>
        /// Test implementation of IObjectData for testing object data overloads
        /// </summary>
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
    }
}
