/*
* Copyright (c) 2024 Vaughn Nugent
* 
* Library: VNLib
* Package: VNLib.Data.Caching.Extensions
* File: VNCacheClusterClient.cs 
*
* VNCacheClusterClient.cs is part of VNLib.Data.Caching.Extensions which is part of the larger 
* VNLib collection of libraries and utilities.
*
* VNLib.Data.Caching.Extensions is free software: you can redistribute it and/or modify 
* it under the terms of the GNU Affero General Public License as 
* published by the Free Software Foundation, either version 3 of the
* License, or (at your option) any later version.
*
* VNLib.Data.Caching.Extensions is distributed in the hope that it will be useful,
* but WITHOUT ANY WARRANTY; without even the implied warranty of
* MERCHANTABILITY or FITNESS FOR A PARTICULAR PURPOSE. See the
* GNU Affero General Public License for more details.
*
* You should have received a copy of the GNU Affero General Public License
* along with this program.  If not, see https://www.gnu.org/licenses/.
*/

using System;
using System.Security;
using System.Threading;
using System.Threading.Tasks;

using VNLib.Net.Messaging.FBM;
using VNLib.Net.Messaging.FBM.Client;
using VNLib.Data.Caching.Extensions.Clustering;

namespace VNLib.Data.Caching.Extensions
{
    /// <summary>
    /// Manages client connections to cluster nodes with discovery from <see cref="VNCacheClusterManager"/>
    /// instance.
    /// </summary>
    /// <param name="config">The client configuration to use when discovering or connecting to cache nodes</param>
    /// <param name="factory">The fbm client factory instance</param>
    public class VNCacheClusterClient(CacheClientConfiguration config, FBMClientFactory factory)
       : VNCacheClusterManager(config)
    {

        /// <summary>
        /// Connects to the specified server on the configured cache client
        /// </summary>
        /// <param name="server">The server to connect to</param>
        /// <param name="token">A token to cancel the operation</param>
        /// <returns>A task that resolves when the client is connected to the cache server</returns>
        /// <exception cref="FBMException"></exception>
        /// <exception cref="FBMServerNegiationException"></exception>
        /// <exception cref="ArgumentException"></exception>
        /// <exception cref="ArgumentNullException"></exception>
        /// <exception cref="SecurityException"></exception>
        /// <exception cref="ObjectDisposedException"></exception>
        public async Task<FBMClient> ConnectToCacheAsync(CacheNodeAdvertisment server, CancellationToken token = default)
        {
            ArgumentNullException.ThrowIfNull(server);

            FBMClient client = factory.CreateClient();

            try
            {
                //Connect to server (no server id because client not replication server)
                await client.ConnectToCacheAsync(server, config, token);
                return client;
            }
            catch
            {
                client.Dispose();
                throw;
            }
        }
    }
}
