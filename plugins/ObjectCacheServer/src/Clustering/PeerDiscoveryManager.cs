﻿/*
* Copyright (c) 2024 Vaughn Nugent
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

using VNLib.Utils.Logging;
using VNLib.Plugins.Extensions.Loading;
using VNLib.Data.Caching.Extensions;
using VNLib.Data.Caching.Extensions.Clustering;

namespace VNLib.Data.Caching.ObjectCache.Server.Clustering
{

    /*
     * This class is responsible for resolving and discovering peer nodes in the cluster network.
     */

    internal sealed class PeerDiscoveryManager(NodeConfig config, ILogProvider Log, bool IsDebug, bool HasWellKnown) : IAsyncBackgroundWork, ICachePeerAdapter
    {
        internal const string LOG_SCOPE_NAME = "DISC";

        /*
         * The initial discovery delay. This allows for the server to initialize before
         * starting the discovery process. This will probably be a shorter delay 
         * than a usual discovery interval.
         */
        private static readonly TimeSpan InitialDelay = TimeSpan.FromSeconds(15);
        private static readonly TimeSpan WhenWellKnownResolveFailed = TimeSpan.FromSeconds(20);

        private readonly List<CacheNodeAdvertisment> _connectedPeers = [];
        private readonly CachePeerMonitor Monitor = new();
        private readonly VNCacheClusterManager clusterMan = new(config.Config);

        async Task IAsyncBackgroundWork.DoWorkAsync(ILogProvider pluginLog, CancellationToken exitToken)
        {
            /*
             * This loop uses the peer monitor to keep track of all connected peers, then gets 
             * all the advertising peers (including known peers) and resolves all nodes across 
             * the network. 
             */

            //Start the change listener 
            Task watcher = WatchForPeersAsync(exitToken);

            Log.Information("Node discovery worker started, waiting for {idel} to start initial discovery", InitialDelay);

            try
            {
                //Wait for the initial delay
                await Task.Delay(InitialDelay, exitToken);

                Log.Debug("Begining discovery loop");

                while (true)
                {
                    bool wellKnownFailed = false;

                    try
                    {
                        if (IsDebug)
                        {
                            Log.Debug("Begining node discovery");
                        }

                        //Resolve all known peers
                        CacheNodeAdvertisment[] wellKnown = await clusterMan.ResolveWellKnownAsync(exitToken);
                        wellKnownFailed = wellKnown.Length == 0;

                        //Use the monitor to get the initial peers
                        IEnumerable<CacheNodeAdvertisment> ads = GetMonitorAds();

                        //Combine well-known with new connected peers
                        CacheNodeAdvertisment[] allAds = ads.Union(wellKnown).ToArray();

                        if (allAds.Length > 0)
                        {
                            //Discover all known nodes
                            await clusterMan.DiscoverNodesAsync(allAds, exitToken);
                        }

                        //Log the discovered nodes if verbose logging is enabled
                        if (IsDebug)
                        {
                            CacheNodeAdvertisment[] found = clusterMan.DiscoveredNodes.GetAllNodes();

                            Log.Debug("Discovered {count} nodes\n\t{nodes}", found.Length, found.Select(static s => s.NodeId));
                        }
                    }
                    catch(OperationCanceledException)
                    {
                        throw;
                    }
                    catch (Exception ex)
                    {
                        Log.Error(ex, "Failed to discover new peer nodes");
                    }

                    /*
                     * If we have well known nodes and the discovery failed, we wait for a shorter
                     * duration before retrying. This is to avoid spamming the network with requests
                     * if the well known nodes are down. But if we don't have any well known nodes
                     * we cannot continue. 
                     * 
                     * This only matters if we are exepcted to have well known nodes.
                     */
                    if(HasWellKnown && wellKnownFailed)
                    {
                        if (IsDebug)
                        {
                            Log.Debug("Well known node discovery failed, waiting for {idel} before retrying", WhenWellKnownResolveFailed);
                        }

                        //Wait for shorter duration
                        await Task.Delay(WhenWellKnownResolveFailed, exitToken);
                    }
                    else
                    {
                        //Delay the next discovery
                        await Task.Delay(config.DiscoveryInterval, exitToken);
                    }
                }
            }
            catch (OperationCanceledException)
            {
                //Normal exit
                Log.Information("Node discovery worker exiting on plugin exit");
            }
            finally
            {
                Monitor.Dispose();
            }

            //Wait for the watcher to exit
            await watcher.ConfigureAwait(false);
        }

        private IEnumerable<CacheNodeAdvertisment> GetMonitorAds()
        {
            string selfId = (clusterMan.Config as CacheNodeConfiguration)!.NodeId;
            return Monitor.GetAllPeers()
                    .Where(static p => p.Advertisment != null)
                    //Without us
                    .Where(n => !string.Equals(n.NodeId, selfId, StringComparison.OrdinalIgnoreCase))
                    .Select(static p => p.Advertisment!);
        }

        //Wait for new peers and update the collection
        private async Task WatchForPeersAsync(CancellationToken cancellation)
        {
            try
            {
                Log.Debug("Discovery worker waiting for new peers to connect");

                while (true)
                {

                    //Wait for changes, then get new peers
                    await Monitor.WaitForChangeAsync().WaitAsync(cancellation);

                    Log.Verbose("New peers connected");

                    //Use the monitor to get the initial peers
                    IEnumerable<CacheNodeAdvertisment> ads = GetMonitorAds();

                    clusterMan.AddManualNodes(ads);
                }
            }
            catch (OperationCanceledException)
            {
                //Normal ext
                Log.Debug("Connected peer listener exited");
            }
        }
        

        ///<inheritdoc/>
        public CacheNodeAdvertisment[] GetNewPeers()
        {
            lock (_connectedPeers)
            {
                //Get all discovered peers
                CacheNodeAdvertisment[] peers = clusterMan.DiscoveredNodes.GetAllNodes();

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
    }
}
