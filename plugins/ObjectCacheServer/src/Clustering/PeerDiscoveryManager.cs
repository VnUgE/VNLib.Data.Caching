/*
* Copyright (c) 2023 Vaughn Nugent
* 
* Library: VNLib
* Package: ObjectCacheServer
* File: PeerDiscoveryManager.cs 
*
* PeerDiscoveryManager.cs is part of ObjectCacheServer which is part of the larger 
* VNLib collection of libraries and utilities.
*
* ObjectCacheServer is free software: you can redistribute it and/or modify 
* it under the terms of the GNU Affero General Public License as 
* published by the Free Software Foundation, either version 3 of the
* License, or (at your option) any later version.
*
* ObjectCacheServer is distributed in the hope that it will be useful,
* but WITHOUT ANY WARRANTY; without even the implied warranty of
* MERCHANTABILITY or FITNESS FOR A PARTICULAR PURPOSE. See the
* GNU Affero General Public License for more details.
*
* You should have received a copy of the GNU Affero General Public License
* along with this program.  If not, see https://www.gnu.org/licenses/.
*/

using System;
using System.Linq;
using System.Net.Http;
using System.Threading;
using System.Net.Sockets;
using System.Threading.Tasks;
using System.Collections.Generic;

using VNLib.Utils.Logging;
using VNLib.Data.Caching.Extensions;
using VNLib.Plugins;
using VNLib.Plugins.Extensions.Loading;
using VNLib.Data.Caching.Extensions.Clustering;

namespace VNLib.Data.Caching.ObjectCache.Server.Clustering
{

    /*
     * This class is responsible for resolving and discovering peer nodes in the cluster network.
     */

    internal sealed class PeerDiscoveryManager : IAsyncBackgroundWork, ICachePeerAdapter
    {
        private const string LOG_SCOPE_NAME = "DISC";
        private static readonly TimeSpan InitialDelay = TimeSpan.FromSeconds(15);

        private readonly List<CacheNodeAdvertisment> _connectedPeers;
        private readonly NodeConfig _config;
        private readonly CachePeerMonitor _monitor;
        private readonly bool IsDebug;
        private readonly ILogProvider _log;

        public PeerDiscoveryManager(PluginBase plugin)
        {
            //Get config
            _config = plugin.GetOrCreateSingleton<NodeConfig>();

            //Get the known peers array from config, its allowed to be null for master nodes
            IConfigScope? config = plugin.TryGetConfig("known_peers");
            string[] kownPeers = config?.Deserialze<string[]>() ?? Array.Empty<string>();

            //Add known peers to the monitor
            _config.Config.WithInitialPeers(kownPeers.Select(static s => new Uri(s)));

            plugin.Log.Information("Inital peer nodes: {nodes}", kownPeers);

            //Get the peer monitor
            _monitor = plugin.GetOrCreateSingleton<CachePeerMonitor>();
            
            _connectedPeers = new();

            //Create scoped logger
            _log = plugin.Log.CreateScope(LOG_SCOPE_NAME);

            //Setup discovery error handler
            _config.Config.WithErrorHandler(new ErrorHandler(_log));

            IsDebug = plugin.IsDebug();
        }

        async Task IAsyncBackgroundWork.DoWorkAsync(ILogProvider pluginLog, CancellationToken exitToken)
        {
            /*
             * This loop uses the peer monitor to keep track of all connected peers, then gets 
             * all the advertising peers (including known peers) and resolves all nodes across 
             * the network. 
             */

            //Start the change listener 
            Task watcher = WatchForPeersAsync(exitToken);

            _log.Information("Node discovery worker started, waiting for {idel} to start initial discovery", InitialDelay);

            try
            {
                //Wait for the initial delay
                await Task.Delay(InitialDelay, exitToken);

                _log.Debug("Begining discovery loop");

                /*
                 * To avoid connecting to ourself, we add ourselves to the connected list
                 * and it should never get removed. This is because the monitor will never
                 * report our own advertisment.
                 */
                _connectedPeers.Add(_config.Config.Advertisment);

                while (true)
                {
                    try
                    {
                        if (IsDebug)
                        {
                            _log.Debug("Begining node discovery");
                        }

                        //Resolve all known peers
                        CacheNodeAdvertisment[] wellKnown = await _config.Config.ResolveWellKnownAsync(exitToken);

                        //Use the monitor to get the initial peers
                        IEnumerable<CacheNodeAdvertisment> ads = GetMonitorAds();

                        //Combine well-known with new connected peers
                        CacheNodeAdvertisment[] allAds = ads.Union(wellKnown).ToArray();

                        if (allAds.Length > 0)
                        {
                            //Discover all kown nodes
                            await _config.Config.DiscoverNodesAsync(allAds, exitToken);
                        }

                        //Log the discovered nodes if verbose logging is enabled
                        if (IsDebug)
                        {
                            CacheNodeAdvertisment[] found = _config.Config.NodeCollection.GetAllNodes();

                            _log.Debug("Discovered {count} nodes\n\t{nodes}", found.Length, found.Select(static s => s.NodeId));
                        }
                    }
                    catch(OperationCanceledException)
                    {
                        throw;
                    }
                    catch (Exception ex)
                    {
                        _log.Error(ex, "Failed to discover new peer nodes");
                    }

                    //Delay the next discovery
                    await Task.Delay(_config.DiscoveryInterval, exitToken);
                }
            }
            catch (OperationCanceledException)
            {
                //Normal exit
                _log.Information("Node discovery worker exiting");
            }
            finally
            {

            }

            //Wait for the watcher to exit
            await watcher.ConfigureAwait(false);
        }

        private IEnumerable<CacheNodeAdvertisment> GetMonitorAds()
        {
            return _monitor.GetAllPeers()
                    .Where(static p => p.Advertisment != null)
                    //Without us
                    .Where(n => n.NodeId != _config.Config.NodeId)
                    .Select(static p => p.Advertisment!);
        }

        //Wait for new peers and update the collection
        private async Task WatchForPeersAsync(CancellationToken cancellation)
        {
            try
            {
                _log.Debug("Discovery worker waiting for new peers to connect");

                while (true)
                {

                    //Wait for changes, then get new peers
                    await _monitor.WaitForChangeAsync().WaitAsync(cancellation);

                    _log.Verbose("New peers connected");

                    //Use the monitor to get the initial peers
                    IEnumerable<CacheNodeAdvertisment> ads = GetMonitorAds();

                    ((NodeDiscoveryCollection)_config.Config.NodeCollection).AddManualNodes(ads);
                }
            }
            catch (OperationCanceledException)
            {
                //Normal ext
                _log.Debug("Connected peer listener exited");
            }
        }
        

        ///<inheritdoc/>
        public CacheNodeAdvertisment[] GetNewPeers()
        {
            lock (_connectedPeers)
            {
                //Get all discovered peers
                CacheNodeAdvertisment[] peers = _config.Config.NodeCollection.GetAllNodes();

                //Get the difference between the discovered peers and the connected peers
                return peers.Except(_connectedPeers).ToArray();
            }
        }

        ///<inheritdoc/>
        public void OnPeerListenerAttached(CacheNodeAdvertisment peer)
        {
            lock (_connectedPeers)
            {
                //Add to connected peers
                _connectedPeers.Add(peer);
            }
        }

        ///<inheritdoc/>
        public void OnPeerListenerDetatched(CacheNodeAdvertisment peer)
        {
            //remove from connected peers
            lock (_connectedPeers)
            {
                _connectedPeers.Remove(peer);
            }
        }


        private sealed record class ErrorHandler(ILogProvider Logger) : ICacheDiscoveryErrorHandler
        {
            public void OnDiscoveryError(CacheNodeAdvertisment errorNode, Exception ex)
            {

                if (ex is HttpRequestException hre)
                {
                    if (hre.InnerException is SocketException se)
                    {
                        //traisnport failed
                        Logger.Warn("Failed to connect to server {serv} because {err}", errorNode, se.Message);
                    }
                    else
                    {
                        Logger.Error("Failed to connect to node {n}\n{err}", errorNode, hre);
                    }
                }
                else
                {
                    Logger.Error("Failed to discover nodes from nodeid {nid}, with error\n{err}", errorNode, ex);
                }
                
            }
        }
    }
}
