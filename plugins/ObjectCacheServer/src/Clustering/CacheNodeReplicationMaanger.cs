﻿/*
* Copyright (c) 2023 Vaughn Nugent
* 
* Library: VNLib
* Package: ObjectCacheServer
* File: CacheNodeReplicationMaanger.cs 
*
* CacheNodeReplicationMaanger.cs is part of ObjectCacheServer which is part 
* of the larger VNLib collection of libraries and utilities.
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
using static VNLib.Data.Caching.Constants;
using VNLib.Net.Messaging.FBM;
using VNLib.Net.Messaging.FBM.Client;
using VNLib.Plugins.Extensions.Loading;
using VNLib.Data.Caching.Extensions.Clustering;
using VNLib.Data.Caching.ObjectCache.Server.Cache;

namespace VNLib.Data.Caching.ObjectCache.Server.Clustering
{

    /*
     * This class is responsible for replicating the cache with other nodes.
     * 
     * It does this by connecting to other nodes and listening for change events. 
     * When a change event occurs, it takes action against the local cache store, 
     * to keep it consistent with the other nodes.
     * 
     * Change events are only handled first-hand, meaning that events do not 
     * propagate to other nodes, they must be connected individually to each node
     * and listen for changes.
     */

    internal sealed class CacheNodeReplicationMaanger : IAsyncBackgroundWork
    {
        private const string LOG_SCOPE_NAME = "REPL";

        private static readonly TimeSpan GetItemTimeout = TimeSpan.FromSeconds(10);
        private const int MAX_MESSAGE_SIZE = 12 * 1024;

        private readonly PluginBase _plugin;
        private readonly ILogProvider _log;
        private readonly NodeConfig _nodeConfig;
        private readonly ICacheStore _cacheStore;
        private readonly ICachePeerAdapter _peerAdapter;
        private readonly FBMClientConfig _replicationClientConfig;
       
        private readonly bool _isDebug;

        private int _openConnections;

        public CacheNodeReplicationMaanger(PluginBase plugin)
        {
            //Load the node config
            _nodeConfig = plugin.GetOrCreateSingleton<NodeConfig>();
            _cacheStore = plugin.GetOrCreateSingleton<CacheStore>();
            _peerAdapter = plugin.GetOrCreateSingleton<PeerDiscoveryManager>();            

            //Init fbm config with fixed message size
            _replicationClientConfig = FBMDataCacheExtensions.GetDefaultConfig(
                (plugin as ObjectCacheServerEntry)!.CacheHeap,
                MAX_MESSAGE_SIZE,
                debugLog: plugin.IsDebug() ? plugin.Log : null
            );

            _plugin = plugin;
            _isDebug = plugin.IsDebug();
            _log = plugin.Log.CreateScope(LOG_SCOPE_NAME);
        }

        public async Task DoWorkAsync(ILogProvider pluginLog, CancellationToken exitToken)
        {
            _log.Information("Initializing node replication worker");

            try
            {
                while (true)
                {
                    //Get all new peers
                    CacheNodeAdvertisment[] peers = _peerAdapter.GetNewPeers();

                    if (peers.Length == 0 && _isDebug)
                    {
                        _log.Verbose("No new peers to connect to");
                    }

                    //Make sure we don't exceed the max connections
                    if(_openConnections >= _nodeConfig.MaxPeerConnections)
                    {
                        if (_isDebug)
                        {
                            _log.Verbose("Max peer connections reached, waiting for a connection to close");
                        }
                    }
                    else
                    {
                        //Connect to each peer as a background task
                        foreach (CacheNodeAdvertisment peer in peers)
                        {
                            _ = _plugin.ObserveWork(() => OnNewPeerDoWorkAsync(peer, _log, exitToken));
                        }
                    }

                    //Wait for a new peers
                    await Task.Delay(10000, exitToken);
                }
            }
            catch (OperationCanceledException)
            {
                //Normal exit
            }
            catch
            {
                _log.Error("Node replication worker exited with an error");
                throw;
            }
            finally
            {

            }

            _log.Information("Node replication worker exited");
        }

        private async Task OnNewPeerDoWorkAsync(CacheNodeAdvertisment newPeer, ILogProvider log, CancellationToken exitToken)
        {
            _ = newPeer ?? throw new ArgumentNullException(nameof(newPeer));

            //Setup client 
            FBMClient client = new(_replicationClientConfig);

            //Add peer to monitor
            _peerAdapter.OnPeerListenerAttached(newPeer);

            Interlocked.Increment(ref _openConnections);

            try
            {
                log.Information("Establishing replication connection to peer {server}...", newPeer.NodeId);

                //Connect to the server
                await client.ConnectToCacheAsync(newPeer, _nodeConfig.Config, exitToken);

                log.Information("Connected to {server}, starting queue listeners", newPeer.NodeId);

                //Start worker tasks
                List<Task> workerTasks = new();

                for (int i = 0; i < Environment.ProcessorCount; i++)
                {
                    Task workerTask = Task.Run(() => ReplicationWorkerDoWorkAsync(client, log, exitToken), exitToken);

                    workerTasks.Add(workerTask);
                }

                //Wait for sync workers to exit
                await Task.WhenAll(workerTasks);

                log.Debug("All cache worker tasks exited successfully, disconnecting from {server}", newPeer.NodeId);

                //Disconnect client gracefully
                await client.DisconnectAsync(CancellationToken.None);
            }
            catch (InvalidResponseException ie)
            {
                //See if the plugin is unloading
                if (!exitToken.IsCancellationRequested)
                {
                    log.Debug("Peer {p} responded with invalid response packet, disconnected. reason\n {reason}", newPeer.NodeId, ie);
                }
                //Disconnect client gracefully
                try
                {
                    await client.DisconnectAsync(CancellationToken.None);
                }
                catch (Exception ex)
                {
                    log.Error(ex);
                }
            }
            catch (OperationCanceledException)
            {
                //Plugin unloading, Try to disconnect 
                try
                {
                    await client.DisconnectAsync(CancellationToken.None);
                }
                catch (Exception ex)
                {
                    log.Error(ex);
                }
            }
            catch (Exception ex)
            {
                log.Warn("Lost connection to peer {h}\n {m}", newPeer.NodeId, ex);
            }
            finally
            {
                Interlocked.Decrement(ref _openConnections);

                client.Dispose();

                //Notify monitor of disconnect
                _peerAdapter.OnPeerListenerDetatched(newPeer);
            }
        }

        //Wroker task callback method
        private async Task ReplicationWorkerDoWorkAsync(FBMClient client, ILogProvider log, CancellationToken exitToken)
        {
            //Reusable request message
            using FBMRequest request = new(client.Config);

            //Listen for changes
            while (true)
            {
                //Wait for changes
                WaitForChangeResult changedObject = await client.WaitForChangeAsync(exitToken);

                log.Debug("Object changed {typ} {obj}", changedObject.Status, changedObject.CurrentId);

                switch (changedObject.Status)
                {
                    case ResponseCodes.NotFound:
                        log.Error("Server cache not properly configured, worker exiting");
                        return;
                    case "deleted":
                        //Delete the object from the store
                        await _cacheStore.DeleteItemAsync(changedObject.CurrentId, CancellationToken.None);
                        break;
                    case "modified":
                        //Reload the record from the store
                        await UpdateRecordAsync(client, request, log, changedObject.CurrentId, changedObject.NewId, exitToken);
                        break;
                }

                //Reset request message
                request.Reset();
            }
        }

        private async Task UpdateRecordAsync(FBMClient client, FBMRequest modRequest, ILogProvider log, string objectId, string newId, CancellationToken cancellation)
        {
            //Set action as get/create
            modRequest.WriteHeader(HeaderCommand.Action, Actions.Get);
            //Set session-id header
            modRequest.WriteHeader(ObjectId, string.IsNullOrWhiteSpace(newId) ? objectId : newId);

            //Make request
            using FBMResponse response = await client.SendAsync(modRequest, GetItemTimeout, cancellation);

            response.ThrowIfNotSet();

            //Check response code
            string status = response.Headers.First(static s => s.Header == HeaderCommand.Status).Value.ToString();

            if (ResponseCodes.Okay.Equals(status, StringComparison.Ordinal))
            {
                //Update the record
                await _cacheStore.AddOrUpdateBlobAsync(objectId, newId, static (t) => t.ResponseBody, response, cancellation);
                log.Debug("Updated object {id}", objectId);
            }
            else
            {
                log.Warn("Object {id} was missing on the remote server", objectId);
            }
        }
    }
}