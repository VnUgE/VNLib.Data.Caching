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
using System.Threading;
using System.Threading.Tasks;
using System.Collections.Generic;

using VNLib.Plugins;
using VNLib.Utils.Logging;
using VNLib.Data.Caching.Extensions;
using VNLib.Plugins.Extensions.Loading;


namespace VNLib.Data.Caching.ObjectCache.Server.Distribution
{

    sealed class PeerDiscoveryManager : IAsyncBackgroundWork, ICachePeerAdapter
    {
        private readonly List<ICacheNodeAdvertisment> _connectedPeers;
        private readonly NodeConfig _config;
        private readonly IPeerMonitor _monitor;      
        private readonly KnownPeerList _knownPeers;

        public PeerDiscoveryManager(PluginBase plugin)
        {
            //Get config
            _config = plugin.GetOrCreateSingleton<NodeConfig>();

            //Get the peer monitor
            _monitor = plugin.GetOrCreateSingleton<CachePeerMonitor>();

            //Get the known peer list
            _knownPeers = plugin.GetOrCreateSingleton<KnownPeerList>();
            
            _connectedPeers = new();

            //Setup discovery error handler
            _config.Config.WithErrorHandler(new ErrorHandler(plugin.Log));
        }

        async Task IAsyncBackgroundWork.DoWorkAsync(ILogProvider pluginLog, CancellationToken exitToken)
        {
            /*
             * This loop uses the peer monitor to keep track of all connected peers, then gets 
             * all the advertising peers (including known peers) and resolves all nodes across 
             * the network. 
             */

            pluginLog.Information("Node discovery worker started");

            try
            {
                while (true)
                {
                    try
                    {
                        //Use the monitor to get the initial peers
                        IEnumerable<ICacheNodeAdvertisment> ads = _monitor.GetAllPeers()
                                                        .Where(static p => p.Advertisment != null)
                                                        .Select(static p => p.Advertisment!);

                        //Add known peers to the initial list
                        ads = ads.Union(_knownPeers.GetPeers());

                        //Set initial peers
                        _config.Config.WithInitialPeers(ads);

                        //Discover all nodes
                        await _config.Config.DiscoverNodesAsync(exitToken);
                    }
                    catch(OperationCanceledException)
                    {
                        throw;
                    }
                    catch (Exception ex)
                    {
                        pluginLog.Error(ex, "Failed to discover new peer nodes");
                    }

                    //Delay the next discovery
                    await Task.Delay(_config.DiscoveryInterval, exitToken);
                }
            }
            catch (OperationCanceledException)
            {
                //Normal exit
                pluginLog.Information("Node discovery worker exiting");
            }
            finally
            {

            }
        }
        

        ///<inheritdoc/>
        public ICacheNodeAdvertisment[] GetNewPeers()
        {
            lock (_connectedPeers)
            {
                //Get all discovered peers
                ICacheNodeAdvertisment[] peers = _config.Config.NodeCollection.GetAllNodes();

                //Get the difference between the discovered peers and the connected peers
                return peers.Except(_connectedPeers).ToArray();
            }
        }

        ///<inheritdoc/>
        public void OnPeerListenerAttached(ICacheNodeAdvertisment peer)
        {
            lock (_connectedPeers)
            {
                //Add to connected peers
                _connectedPeers.Add(peer);
            }
        }

        ///<inheritdoc/>
        public void OnPeerListenerDetatched(ICacheNodeAdvertisment peer)
        {
            //remove from connected peers
            lock (_connectedPeers)
            {
                _connectedPeers.Remove(peer);
            }
        }


        private sealed record class ErrorHandler(ILogProvider Logger) : ICacheDiscoveryErrorHandler
        {
            public void OnDiscoveryError(ICacheNodeAdvertisment errorNode, Exception ex)
            {
                Logger.Error("Failed to discover nodes from nodeid {nid}, with error\n{err}", errorNode, ex);
            }
        }
    }
}
