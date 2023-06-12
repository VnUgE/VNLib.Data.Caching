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
        private readonly NodeConfig _config;
        private readonly IPeerMonitor _monitor;        
        private readonly INodeDiscoveryCollection _peers;

        public PeerDiscoveryManager(PluginBase plugin)
        {
            //Get config
            _config = plugin.GetOrCreateSingleton<NodeConfig>();

            //Get the peer monitor
            _monitor = plugin.GetOrCreateSingleton<CachePeerMonitor>();

            //Get the node collection
            _peers = plugin.GetOrCreateSingleton<NodeDiscoveryCollection>();
            
            _connectedPeers = new();
        }

        async Task IAsyncBackgroundWork.DoWorkAsync(ILogProvider pluginLog, CancellationToken exitToken)
        {
            pluginLog.Information("Node discovery worker started");

            try
            {
                while (true)
                {
                    try
                    {
                        await DiscoverAllNodesAsync(pluginLog, exitToken);
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

        async Task DiscoverAllNodesAsync(ILogProvider log, CancellationToken cancellation)
        {
            //Use the monitor to get the initial peers
            IEnumerable<ICachePeerAdvertisment> ads = _monitor.GetAllPeers()
                                                        .Where(static p => p.Advertisment != null)
                                                        .Select(static p => p.Advertisment!);

            //Init enumerator with initial peers
            INodeDiscoveryEnumerator enumerator = _peers.BeginDiscovery(ads);

            do
            {
                //Load the initial peer
                ICachePeerAdvertisment? peer = enumerator.GetNextPeer();

                if (peer == null)
                {
                    break;
                }

                log.Verbose("Discovering peer nodes from {Peer}", peer.NodeId);

                //Discover nodes from this peer
                ICachePeerAdvertisment[]? newNodes = await _config.Config.DiscoverClusterNodesAsync(peer, cancellation);

                //Add nodes to the enumerator
                if (newNodes != null)
                {
                    enumerator.OnPeerDiscoveryComplete(newNodes);
                }

            } while (true);

            //Commit peer updates
            _peers.CompleteDiscovery(enumerator);
        }


        private readonly List<ICachePeerAdvertisment> _connectedPeers;

        ///<inheritdoc/>
        public ICachePeerAdvertisment[] GetNewPeers()
        {
            lock (_connectedPeers)
            {
                //Get all discovered peers
                ICachePeerAdvertisment[] peers = _peers.GetAllNodes();

                //Get the difference between the discovered peers and the connected peers
                return peers.Except(_connectedPeers).ToArray();
            }
        }

        ///<inheritdoc/>
        public void OnPeerListenerAttached(ICachePeerAdvertisment peer)
        {
            lock (_connectedPeers)
            {
                //Add to connected peers
                _connectedPeers.Add(peer);
            }
        }

        ///<inheritdoc/>
        public void OnPeerListenerDetatched(ICachePeerAdvertisment peer)
        {
            //remove from connected peers
            lock (_connectedPeers)
            {
                _connectedPeers.Remove(peer);
            }
        }
    }
}
