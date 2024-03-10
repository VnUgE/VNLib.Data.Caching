/*
* Copyright (c) 2024 Vaughn Nugent
* 
* Library: VNLib
* Package: VNLib.Data.Caching.Extensions
* File: VNCacheClusterManager.cs 
*
* VNCacheClusterManager.cs is part of VNLib.Data.Caching.Extensions 
* which is part of the larger VNLib collection of libraries and utilities.
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
using System.Linq;
using System.Security;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using System.Collections.Generic;

using RestSharp;

using VNLib.Hashing.IdentityUtility;
using VNLib.Net.Rest.Client.Construction;
using VNLib.Data.Caching.Extensions.ApiModel;
using VNLib.Data.Caching.Extensions.Clustering;

namespace VNLib.Data.Caching.Extensions
{

    /// <summary>
    /// A VNCache cluster client discovery maanger. Used to simplify the discovery 
    /// of cache nodes
    /// </summary>
    /// <param name="config">The client configuration instance</param>
    public class VNCacheClusterManager(CacheClientConfiguration config)
    {
        /// <summary>
        /// The internal collection of discovered nodes
        /// </summary>
        protected NodeDiscoveryCollection NodeCollection { get; } = GetNodeCollection(config);

        /// <summary>
        /// Gets the collection of discovered nodes within the manager
        /// </summary>
        public INodeDiscoveryCollection DiscoveredNodes => NodeCollection;

        /// <summary>
        /// The underlying <see cref="CacheClientConfiguration"/> instance
        /// </summary>
        public CacheClientConfiguration Config => config;

        /// <summary>
        /// Adds an array of nodes manually to the collection of discovered cluster nodes
        /// </summary>
        /// <param name="nodes"></param>
        public void AddManualNodes(params CacheNodeAdvertisment[] nodes) => AddManualNodes(nodes.AsEnumerable());

        /// <summary>
        /// Adds an array of nodes manually to the collection of discovered cluster nodes
        /// </summary>
        /// <param name="nodes"></param>
        public void AddManualNodes(IEnumerable<CacheNodeAdvertisment> nodes) => NodeCollection.AddManualNodes(nodes);

        /// <summary>
        /// Removes an array of nodes manually from the collection of discovered cluster nodes
        /// </summary>
        /// <param name="nodes"></param>
        public void RemoveManualNodes(params CacheNodeAdvertisment[] nodes) => RemoveManualNodes(nodes.AsEnumerable());

        /// <summary>
        /// Removes an array of nodes manually from the collection of discovered cluster nodes
        /// </summary>
        /// <param name="nodes"></param>
        public void RemoveManualNodes(IEnumerable<CacheNodeAdvertisment> nodes) => NodeCollection.RemoveManualNodes(nodes);

        /// <summary>
        /// Resolves the initial well-known cache nodes into their advertisments
        /// </summary>
        /// <param name="cancellation">A token to cancel the operation</param>
        /// <returns>An array of resolved nodes</returns>
        public async Task<CacheNodeAdvertisment[]> ResolveWellKnownAsync(CancellationToken cancellation)
        {
            //Make sure at least one node defined
            if (config?.WellKnownNodes == null || config.WellKnownNodes.Length == 0)
            {
                throw new ArgumentException("There must be at least one cache node defined in the client configuration");
            }

            Task<CacheNodeAdvertisment?>[] initialAdds = new Task<CacheNodeAdvertisment?>[config.WellKnownNodes.Length];

            //Discover initial advertisments from well-known addresses
            for (int i = 0; i < config.WellKnownNodes.Length; i++)
            {
                initialAdds[i] = DiscoverNodeConfigAsync(config.WellKnownNodes[i], cancellation);
            }

            //Wait for all initial adds to complete
            await Task.WhenAll(initialAdds);

            //Get the initial advertisments that arent null
            return initialAdds.Select(static x => x.Result!).Where(static s => s != null).ToArray();
        }

        /// <summary>
        /// Discovers ALL possible cache nodes itteritivley, first by collecting the configuration
        /// from the initial peers.
        /// This will make connections to all discoverable servers
        /// </summary>
        /// <param name="cancellation">A token to cancel the operation</param>
        /// <returns></returns>
        /// <exception cref="ArgumentException"></exception>
        /// <exception cref="CacheDiscoveryFailureException"></exception>
        /// <remarks>
        /// This method simply combines the <see cref="ResolveWellKnownAsync"/> and <see cref="DiscoverNodesAsync"/>
        /// methods into a single operation
        /// </remarks>
        public async Task DiscoverNodesAsync(CancellationToken cancellation)
        {
            //Make sure at least one node defined
            if (config?.WellKnownNodes == null || config.WellKnownNodes.Length == 0)
            {
                throw new ArgumentException("There must be at least one cache node defined in the client configuration");
            }

            /*
             * Connect to well-known nodes from the client configuration to discovery its layout.
             * 
             */
            CacheNodeAdvertisment[] initialPeers = await ResolveWellKnownAsync(cancellation);

            if (initialPeers.Length == 0)
            {
                throw new CacheDiscoveryFailureException("There must be at least one available cache node to continue discovery");
            }

            await DiscoverNodesAsync(initialPeers, cancellation);
        }

        /// <summary>
        /// Discovers ALL possible cache nodes itteritivley from the current collection of initial peers.
        /// This will make connections to all discoverable servers and update the client configuration, with all 
        /// discovered peers
        /// </summary>
        /// <param name="initialPeers">Accepts an array of initial peers to override the endpoint discovery process</param>
        /// <param name="cancellation">A token to cancel the operation</param>
        /// <returns>A task that completes when all nodes have been discovered</returns>
        /// <exception cref="ArgumentException"></exception>
        /// <exception cref="CacheDiscoveryFailureException"></exception>
        public async Task DiscoverNodesAsync(CacheNodeAdvertisment[] initialPeers, CancellationToken cancellation)
        {
            //Make sure at least one node defined
            ArgumentNullException.ThrowIfNull(initialPeers);
            ArgumentOutOfRangeException.ThrowIfZero(initialPeers.Length);

            //Get the discovery enumerator with the initial peers
            using INodeDiscoveryEnumerator enumerator = NodeCollection.BeginDiscovery(initialPeers);

            //Start the discovery process
            await DiscoverNodesAsync(enumerator, config, config.ErrorHandler, cancellation);

            //Commit discovered nodes to stored node collection
            NodeCollection.CompleteDiscovery(enumerator);
        }

        private static async Task DiscoverNodesAsync(
            INodeDiscoveryEnumerator enumerator,
            CacheClientConfiguration config,
            ICacheDiscoveryErrorHandler? errHandler,
            CancellationToken cancellation
        )
        {
            //Loop through servers
            while (enumerator.MoveNext())
            {
                //Make sure the node has a discovery endpoint
                if (enumerator.Current.DiscoveryEndpoint == null)
                {
                    //Skip this node
                    continue;
                }

                /*
                 * We are allowed to save nodes that do not have a discovery endpoint, but we cannot 
                 * discover nodes from them we can only use them as cache
                 */

                //add a random delay to avoid spamming servers
                await Task.Delay((int)Random.Shared.NextInt64(100, 500), cancellation);

                try
                {
                    //Discover nodes from the current node
                    CacheNodeAdvertisment[]? nodes = await GetCacheNodesAsync(enumerator.Current, config, cancellation);

                    if (nodes != null)
                    {
                        //Add nodes to the collection
                        enumerator.OnPeerDiscoveryComplete(nodes);
                    }
                }
                //Catch exceptions when an error handler is defined
                catch (Exception ex) when (errHandler != null)
                {
                    //Handle the error
                    errHandler.OnDiscoveryError(enumerator.Current, ex);
                }
                catch (Exception ex)
                {
                    throw new CacheDiscoveryFailureException($"Failed to discovery peer node {enumerator.Current?.NodeId}, cannot continue", ex);
                }
            }
        }

        /// <summary>
        /// Contacts the given server's discovery endpoint to discover a list of available 
        /// servers we can connect to
        /// </summary>
        /// <param name="advert">An advertisment of a server to discover other nodes from</param>
        /// <param name="cancellationToken">A token to cancel the operationS</param>
        /// <param name="config">The cache configuration object</param>
        /// <returns>The list of active servers</returns>
        /// <exception cref="SecurityException"></exception>
        /// <exception cref="ArgumentException"></exception>
        /// <exception cref="ArgumentNullException"></exception>
        public static async Task<CacheNodeAdvertisment[]?> GetCacheNodesAsync(CacheNodeAdvertisment advert, CacheClientConfiguration config, CancellationToken cancellationToken = default)
        {
            ArgumentNullException.ThrowIfNull(advert);
            ArgumentNullException.ThrowIfNull(config);
            ArgumentNullException.ThrowIfNull(advert.DiscoveryEndpoint, nameof(advert.DiscoveryEndpoint));

            DiscoveryRequest req = new (advert.DiscoveryEndpoint, config);

            //Site adapter verifies response messages so we dont need to check on the response
            byte[] data = await CacheSiteAdapter.Instance.ExecuteAsync(req, cancellationToken).AsBytes()
                ?? throw new InvalidOperationException($"No data returned from node {advert.NodeId}");

            //Response is jwt
            using JsonWebToken responseJwt = JsonWebToken.ParseRaw(data);

            using JsonDocument doc = responseJwt.GetPayload();
            return doc.RootElement.GetProperty("peers").Deserialize<CacheNodeAdvertisment[]>();
        }


        /*
         * This method will connect to a given well-known (cache config endpoint) and discover the 
         * servers configuration (endpoint config) 
         * 
         * This function exists so clients only need a single endpoint to connect to, and the server
         * will return it's signed configuration data (including cluster network information)
         */
        private async Task<CacheNodeAdvertisment?> DiscoverNodeConfigAsync(Uri serverUri, CancellationToken cancellation)
        {
            try
            {
                GetConfigRequest req = new (serverUri, config);

                //Site adapter verifies response messages so we dont need to check on the response
                byte[] data = await CacheSiteAdapter.Instance.ExecuteAsync(req, cancellation).AsBytes()
                        ?? throw new CacheDiscoveryFailureException($"No data returned from desired cache node");

                //Response is jwt
                using JsonWebToken responseJwt = JsonWebToken.ParseRaw(data);

                //The entire payload is just the single serialzed advertisment
                using JsonDocument doc =  responseJwt.GetPayload();

                return doc.RootElement.GetProperty("sub").Deserialize<CacheNodeAdvertisment>();
            }
            //Bypass cdfe when error handler is null (avoid nesting)`
            catch (CacheDiscoveryFailureException) when (config.ErrorHandler == null)
            {
                throw;
            }
            //Catch exceptions when an error handler is defined
            catch (Exception ex) when (config.ErrorHandler != null)
            {
                //Handle the error
                config.ErrorHandler.OnDiscoveryError(serverUri, ex);
                return null;
            }
            catch (Exception ex)
            {
                throw new CacheDiscoveryFailureException("Failed to discover node configuration", ex);
            }
        }

        private static NodeDiscoveryCollection GetNodeCollection(CacheClientConfiguration config)
        {
            return config is CacheNodeConfiguration cnc ? new (cnc.NodeIdRef!) : new (null);
        }
    }
}
