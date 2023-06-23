/*
* Copyright (c) 2023 Vaughn Nugent
* 
* Library: VNLib
* Package: ObjectCacheServer
* File: ObjectCacheServerEntry.cs 
*
* ObjectCacheServerEntry.cs is part of ObjectCacheServer which is part of the larger 
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
using static VNLib.Data.Caching.Constants;
using VNLib.Net.Messaging.FBM;
using VNLib.Net.Messaging.FBM.Client;
using VNLib.Plugins.Extensions.Loading;

namespace VNLib.Data.Caching.ObjectCache.Server.Distribution
{
    internal sealed class CacheNodeReplicationMaanger : IAsyncBackgroundWork
    {
        private static readonly TimeSpan GetItemTimeout = TimeSpan.FromSeconds(10);

        private readonly NodeConfig NodeConfig;
        private readonly ICachePeerAdapter PeerAdapter;
        private readonly ICacheStore CacheStore;
        private readonly FBMClientConfig ClientConfig;
        private readonly PluginBase Plugin;

        private CacheNodeConfiguration CacheConfig => NodeConfig.Config;

        public CacheNodeReplicationMaanger(PluginBase plugin)
        {
            //Load the node config
            NodeConfig = plugin.GetOrCreateSingleton<NodeConfig>();

            //Get peer adapter
            PeerAdapter = plugin.GetOrCreateSingleton<PeerDiscoveryManager>();

            CacheStore = plugin.GetOrCreateSingleton<CacheStore>();
        }

        public async Task DoWorkAsync(ILogProvider pluginLog, CancellationToken exitToken)
        {
            pluginLog.Information("[REPL] Initializing node replication worker");

            try
            {
                while (true)
                {
                    //Get all new peers
                    ICacheNodeAdvertisment[] peers = PeerAdapter.GetNewPeers();

                    if (peers.Length == 0)
                    {
                        pluginLog.Verbose("[REPL] No new peers to connect to");
                    }

                    //Connect to each peer as a background task
                    foreach (ICacheNodeAdvertisment peer in peers)
                    {
                        _ = Plugin.ObserveWork(() => OnNewPeerDoWorkAsync(peer, pluginLog, exitToken));
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
                pluginLog.Error("[REPL] Node replication worker exited with an error");
                throw;
            }
            finally
            {

            }

            pluginLog.Information("[REPL] Node replication worker exited");
        }

        private async Task OnNewPeerDoWorkAsync(ICacheNodeAdvertisment newPeer, ILogProvider log, CancellationToken exitToken)
        {
            _ = newPeer ?? throw new ArgumentNullException(nameof(newPeer));

            //Setup client 
            FBMClient client = new(ClientConfig);

            //Add peer to monitor
            PeerAdapter.OnPeerListenerAttached(newPeer);

            try
            {
                log.Information("Establishing replication connection to peer {server}...", newPeer.NodeId);

                //Connect to the server
                await client.ConnectToCacheAsync(newPeer, CacheConfig, exitToken);

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
                client.Dispose();

                //Notify monitor of disconnect
                PeerAdapter.OnPeerListenerDetatched(newPeer);
            }
        }

        //Wroker task callback method
        private async Task ReplicationWorkerDoWorkAsync(FBMClient client, ILogProvider log, CancellationToken exitToken)
        {
            //Listen for changes
            while (true)
            {
                //Wait for changes
                WaitForChangeResult changedObject = await client.WaitForChangeAsync(exitToken);

                log.Debug("Object changed {typ} {obj}", changedObject.Status, changedObject.CurrentId);

                switch (changedObject.Status)
                {
                    case ResponseCodes.NotFound:
                        log.Warn("Server cache not properly configured, worker exiting");
                        return;
                    case "deleted":
                        //Delete the object from the store
                        await CacheStore.DeleteItemAsync(changedObject.CurrentId, CancellationToken.None);
                        break;
                    case "modified":
                        //Reload the record from the store
                        await UpdateRecordAsync(client, log, changedObject.CurrentId, changedObject.NewId, exitToken);
                        break;
                }
            }
        }

        private async Task UpdateRecordAsync(FBMClient client, ILogProvider log, string objectId, string newId, CancellationToken cancellation)
        {
            //Get request message
            FBMRequest modRequest = client.RentRequest();
            try
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
                    await CacheStore.AddOrUpdateBlobAsync(objectId, newId, static (t) => t.ResponseBody, response, cancellation);
                    log.Debug("Updated object {id}", objectId);
                }
                else
                {
                    log.Warn("Object {id} was missing on the remote server", objectId);
                }
            }
            finally
            {
                client.ReturnRequest(modRequest);
            }
        }
    }
}
