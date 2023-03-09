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
using System.IO;
using System.Net;
using System.Linq;
using System.Net.Http;
using System.Text.Json;
using System.Threading;
using System.Net.Sockets;
using System.Threading.Tasks;
using System.Collections.Generic;
using System.Security.Cryptography;

using VNLib.Plugins;
using VNLib.Utils.Memory;
using VNLib.Utils.Logging;
using VNLib.Utils.Memory.Diagnostics;
using VNLib.Hashing;
using VNLib.Hashing.IdentityUtility;
using VNLib.Data.Caching.Extensions;
using static VNLib.Data.Caching.Constants;
using VNLib.Net.Messaging.FBM;
using VNLib.Net.Messaging.FBM.Client;
using VNLib.Plugins.Cache.Broker.Endpoints;
using VNLib.Plugins.Extensions.Loading;
using VNLib.Plugins.Extensions.Loading.Routing;


namespace VNLib.Data.Caching.ObjectCache.Server
{
    public sealed class ObjectCacheServerEntry : PluginBase
    {
        public override string PluginName => "ObjectCache.Service";

        private readonly Lazy<IUnmangedHeap> _cacheHeap;
        private readonly object ServerLock;
        private readonly HashSet<ActiveServer> ListeningServers;
        private readonly ManualResetEvent BrokerSyncHandle;

        /// <summary>
        /// Gets the shared heap for the plugin 
        /// </summary>
        internal IUnmangedHeap CacheHeap => _cacheHeap.Value;

        public ObjectCacheServerEntry()
        {
            //Init heap
            _cacheHeap = new Lazy<IUnmangedHeap>(InitializeHeap, LazyThreadSafetyMode.PublicationOnly);

            ServerLock = new();
            ListeningServers = new();

            //Set sync handle
            BrokerSyncHandle = new(false);
        }

        private IUnmangedHeap InitializeHeap()
        {
            //Create default heap
            IUnmangedHeap _heap = MemoryUtil.InitializeNewHeapForProcess();
            try
            {
                //If the plugin is in debug mode enable heap tracking
                return this.IsDebug() ? new TrackedHeapWrapper(_heap) : _heap;
            }
            catch
            {
                _heap.Dispose();
                throw;
            }
        }


        private string? BrokerHeartBeatToken;

        private void RemoveServer(ActiveServer server)
        {
            lock (ServerLock)
            {
                ListeningServers.Remove(server);
            }
        }

        private FBMClientConfig ClientConfig;


        protected override void OnLoad()
        {
            try
            {
                IConfigScope clusterConf = this.GetConfig("cluster");

                Uri brokerAddress = new(clusterConf["broker_address"].GetString() ?? throw new KeyNotFoundException("Missing required key 'broker_address' for config 'cluster'"));             

                //Init connect endpoint
                ConnectEndpoint endpoint = this.Route<ConnectEndpoint>();

                //Get the cache store from the connection endpoint
                ICacheStore store = endpoint.GetCacheStore();

                ulong maxByteSize = ((ulong)endpoint.CacheConfig.MaxCacheEntries * (ulong)endpoint.CacheConfig.BucketCount * (ulong)endpoint.CacheConfig.MaxMessageSize);

                //Log max memory usage
                Log.Debug("Maxium memory consumption {mx}Mb", maxByteSize / (ulong)(1024 * 1000));

                //Setup broker and regitration
                {
                    //Route the broker endpoint
                    BrokerHeartBeat brokerEp = new(() => BrokerHeartBeatToken!, BrokerSyncHandle, brokerAddress, this);
                    Route(brokerEp);
                    
                    //start registration 
                    _ = this.ObserveWork(() => RegisterServerAsync(endpoint.Path), 200);
                }
                
                //Setup cluster worker
                {
                    TimeSpan timeout = TimeSpan.FromSeconds(10);

                    //Get pre-configured fbm client config for caching
                    ClientConfig = FBMDataCacheExtensions.GetDefaultConfig(CacheHeap, endpoint.CacheConfig.MaxMessageSize / 2, timeout, this.IsDebug() ? Log : null);

                    //Start Client runner
                    _ = this.ObserveWork(() => RunClientAsync(store, brokerAddress), 300);
                }
                
                //Load a cache broker to the current server if the config is defined
                {
                    if(this.HasConfigForType<BrokerRegistrationEndpoint>())
                    {
                        this.Route<BrokerRegistrationEndpoint>();
                    }
                }

                Log.Information("Plugin loaded");
            }
            catch (KeyNotFoundException kne)
            {
                Log.Error("Missing required configuration variables {m}", kne.Message);
            }
        }

        protected override void OnUnLoad()
        {
            //dispose heap if initialized
            if(_cacheHeap.IsValueCreated)
            {
                _cacheHeap.Value.Dispose();
            }

            //Dispose mre sync handle
            BrokerSyncHandle.Dispose();

            Log.Information("Plugin unloaded");
        }

        #region Registration

        private async Task RegisterServerAsync(string connectPath)
        {
            try
            {
                //Get the broker config element
                IConfigScope clusterConfig = this.GetConfig("cluster");                
                
                //Server id is just dns name for now
                string serverId = Dns.GetHostName();

                int heartBeatDelayMs = clusterConfig["heartbeat_timeout_sec"].GetInt32() * 1000;
               
                
                //Get the port of the primary webserver
                int port;
                bool usingTls;
                {
                    JsonElement firstHost = HostConfig.GetProperty("virtual_hosts").EnumerateArray().First();

                    port = firstHost.GetProperty("interface")
                            .GetProperty("port")
                            .GetInt32();

                    //If a certificate is specified, tls is enabled on the port
                    usingTls = firstHost.TryGetProperty("cert", out _);
                }

                using BrokerRegistrationRequest request = new();
                {
                    string addr = clusterConfig["broker_address"].GetString() ?? throw new KeyNotFoundException("Missing required key 'broker_address' for config 'cluster'");

                    //Recover the certificate
                    ReadOnlyJsonWebKey cacheCert = await GetCachePrivate();
                    
                    //Init url builder for payload, see if tls is enabled
                    Uri connectAddress = new UriBuilder(usingTls ? Uri.UriSchemeHttps : Uri.UriSchemeHttp, Dns.GetHostName(), port, connectPath).Uri;

                    request.WithBroker(new(addr))
                        .WithRegistrationAddress(connectAddress.ToString())
                        .WithNodeId(serverId)
                        .WithSigningKey(cacheCert, true);
                }

                while (true)
                {
                    try
                    {
                        //Gen a random reg token before registering
                        BrokerHeartBeatToken = RandomHash.GetRandomHex(32);

                        //Assign new hb token
                        request.WithHeartbeatToken(BrokerHeartBeatToken);

                        Log.Information("Registering with cache broker {addr}, with node-id {id}", request.BrokerAddress, serverId);

                        //Register with the broker
                        await FBMDataCacheExtensions.ResgisterWithBrokerAsync(request);
                        
                        Log.Debug("Successfully registered with cache broker");

                        /*
                         * Wait in a loop for the broker to send a keepalive
                         * request with the specified token. When the event 
                         * is signaled the task will be completed
                         */
                        while (true)
                        {
                            await Task.Delay(heartBeatDelayMs, UnloadToken);

                            //Set the timeout to 0 to it will just check the status without blocking
                            if (!BrokerSyncHandle.WaitOne(0))
                            {
                                //server miseed a keepalive event, time to break the loop and retry
                                Log.Debug("Broker missed a heartbeat request, attempting to re-register");
                                break;
                            }

                            //Reset the msr
                            BrokerSyncHandle.Reset();
                        }
                    }
                    catch (TaskCanceledException)
                    {
                        throw;
                    }
                    catch (TimeoutException)
                    {
                        Log.Warn("Failed to connect to cache broker server within the specified timeout period");
                    }
                    catch (HttpRequestException re) when (re.InnerException is SocketException)
                    {
                        Log.Warn("Cache broker is unavailable or network is unavailable");
                    }
                    catch (Exception ex)
                    {
                        Log.Warn(ex, "Failed to update broker registration");
                    }
                    
                    //Gen random ms delay
                    int randomMsDelay = RandomNumberGenerator.GetInt32(500, 2000);
                    //Delay 
                    await Task.Delay(randomMsDelay, UnloadToken);
                }
            }
            catch (KeyNotFoundException kne)
            {
                Log.Error("Missing required broker configuration variables {ke}", kne.Message);
            }
            catch (TaskCanceledException)
            {
                //Normal unload/exit
            }
            catch (Exception ex)
            {
                Log.Error(ex);
            }
            finally
            {
                BrokerHeartBeatToken = null;
            }
            Log.Debug("Registration worker exited");
        }

        #endregion

        #region Cluster

        private async Task<ReadOnlyJsonWebKey> GetCachePrivate()
        {
            return await this.TryGetSecretAsync("cache_private_key").ToJsonWebKey() ?? throw new KeyNotFoundException("Failed to load the cache private key");
        }

        private async Task<ReadOnlyJsonWebKey> GetBrokerPublic()
        {
            return await this.TryGetSecretAsync("broker_public_key").ToJsonWebKey() ?? throw new KeyNotFoundException("Failed to load the broker's public key");
        }


        /// <summary>
        /// Starts a self-contained process-long task to discover other cache servers
        /// from a shared broker server 
        /// </summary>
        /// <param name="cacheStore">The cache store to synchronize</param>
        /// <param name="brokerAddress">The broker server's address</param>
        /// <param name="serverId">The node-id of the current server</param>
        /// <param name="clientConf">The configuration to use when initializing synchronization clients</param>
        /// <returns>A task that resolves when the plugin unloads</returns>
        private async Task RunClientAsync(ICacheStore cacheStore, Uri brokerAddress)
        {
            TimeSpan noServerDelay = TimeSpan.FromSeconds(10);

            //The node id is just the dns hostname of the current machine
            string nodeId = Dns.GetHostName();

            ListServerRequest listRequest = new(brokerAddress);
            try
            {
                //Get the broker config element
                IConfigScope clusterConf = this.GetConfig("cluster");

                int serverCheckMs = clusterConf["update_interval_sec"].GetInt32() * 1000;

                //Setup signing and verification certificates
                ReadOnlyJsonWebKey cacheSig = await GetCachePrivate();
                ReadOnlyJsonWebKey brokerPub = await GetBrokerPublic();

                //Import certificates
                listRequest.WithVerificationKey(brokerPub)
                            .WithSigningKey(cacheSig);

                //Main event loop
                Log.Information("Begining cluster node discovery");

                ILogProvider? debugLog = this.IsDebug() ? Log : null;
                
                while (true)
                {
                    //Load the server list
                    ActiveServer[]? servers;
                    while (true)
                    {
                        try
                        {
                            debugLog?.Information("[CACHE] Requesting server list from broker");

                            //Get server list
                            servers = await FBMDataCacheExtensions.ListServersAsync(listRequest, UnloadToken);

                            //Servers are loaded, so continue
                            break;
                        }
                        catch(HttpRequestException he) when(he.InnerException is SocketException)
                        {
                            Log.Warn("Failed to connect to cache broker, trying again");
                        }
                        catch (TimeoutException)
                        {
                            Log.Warn("Failed to connect to cache broker server within the specified timeout period");
                        }
                        catch (Exception ex)
                        {
                            Log.Warn(ex, "Failed to get server list from broker");
                        }

                        //Gen random ms delay
                        int randomMsDelay = RandomNumberGenerator.GetInt32(1000, 2000);

                        //Delay 
                        await Task.Delay(randomMsDelay, UnloadToken);
                    }
                    
                    if(servers == null || servers.Length == 0)
                    {
                        Log.Information("No cluster nodes found, retrying");
                        //Delay 
                        await Task.Delay(noServerDelay, UnloadToken);
                        continue;
                    }
                

                    //Lock on sever set while enumerating
                    lock (ServerLock)
                    {
                        //Select servers that are not the current server and are not already being monitored
                        IEnumerable<ActiveServer> serversToConnectTo = servers.Where(s => !nodeId.Equals(s.ServerId, StringComparison.OrdinalIgnoreCase));
                        
                        //Connect to servers
                        foreach (ActiveServer server in serversToConnectTo)
                        {
                            //Make sure were not currently connected to the server
                            if (!ListeningServers.Contains(server))
                            {
                                //Add the server to the set
                                ListeningServers.Add(server);

                                //Run listener background task
                                _ = this.ObserveWork(() => RunSyncTaskAsync(server, cacheStore, nodeId));
                            }
                        }
                    }
                    
                    //Delay until next check cycle
                    await Task.Delay(serverCheckMs, UnloadToken);
                }
            }
            catch (FileNotFoundException)
            {
                Log.Error("Client/cluster private cluster key file was not found or could not be read");
            }
            catch (KeyNotFoundException)
            {
                Log.Error("Missing required cluster configuration varables");
            }
            catch (TaskCanceledException)
            {
                //normal exit/unload
            }
            catch (Exception ex)
            {
                Log.Error(ex);
            }
            finally
            {
                listRequest.Dispose();
            }
            Log.Debug("Cluster sync worker exited");
        }

        private async Task RunSyncTaskAsync(ActiveServer server, ICacheStore cacheStore, string nodeId)
        {
            //Setup client 
            FBMClient client = new(ClientConfig);
            try
            {
                async Task UpdateRecordAsync(string objectId, string newId)
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
                        using FBMResponse response =  await client.SendAsync(modRequest, UnloadToken);
                       
                        response.ThrowIfNotSet();

                        //Check response code
                        string status = response.Headers.First(static s => s.Header == HeaderCommand.Status).Value.ToString();
                        if (ResponseCodes.Okay.Equals(status, StringComparison.Ordinal))
                        {
                            //Update the record
                            await cacheStore.AddOrUpdateBlobAsync(objectId, newId, static (t) => t.ResponseBody, response);
                            Log.Debug("Updated object {id}", objectId);
                        }
                        else
                        {
                            Log.Warn("Object {id} was missing on the remote server", objectId);
                        }
                    }
                    finally
                    {
                        client.ReturnRequest(modRequest);
                    }
                }
               
                {
                    //Sign and verify requests with the cache private key since we are a peer
                    ReadOnlyJsonWebKey cachePriv = await GetCachePrivate();

                    //Configure cache
                    client.GetCacheConfiguration()
                        .WithVerificationKey(cachePriv)
                        .WithSigningCertificate(cachePriv)
                        .WithNodeId(nodeId) //set nodeid since were listening for changes
                        .WithTls(false);
                }

                Log.Information("Connecting to {server}...", server.ServerId);

                //Connect to the server
                await client.ConnectToCacheAsync(server, UnloadToken);

                //Wroker task callback method
                async Task BgWorkerAsync()
                {
                    //Listen for changes
                    while (true)
                    {
                        //Wait for changes
                        WaitForChangeResult changedObject = await client.WaitForChangeAsync(UnloadToken);
                        
                        Log.Debug("Object changed {typ} {obj}", changedObject.Status, changedObject.CurrentId);
                        
                        switch (changedObject.Status)
                        {
                            case ResponseCodes.NotFound:
                                Log.Warn("Server cache not properly configured, worker exiting");
                                return;
                            case "deleted":
                                //Delete the object from the store
                                _ = cacheStore.DeleteItemAsync(changedObject.CurrentId).ConfigureAwait(false);
                                break;
                            case "modified":
                                //Reload the record from the store
                                await UpdateRecordAsync(changedObject.CurrentId, changedObject.NewId);
                                break;
                        }
                    }
                }
                
                Log.Information("Connected to {server}, starting queue listeners", server.ServerId);
                
                //Start worker tasks
                List<Task> workerTasks = new();
                for(int i = 0; i < Environment.ProcessorCount; i++)
                {
                    workerTasks.Add(Task.Run(BgWorkerAsync));
                }
                
                //Wait for sync workers to exit
                await Task.WhenAll(workerTasks);
            }
            catch (InvalidResponseException ie)
            {
                //See if the plugin is unloading
                if (!UnloadToken.IsCancellationRequested)
                {
                    Log.Debug("Server responded with invalid response packet, disconnected. reason {reason}", ie);
                }
                //Disconnect client gracefully
                try
                {
                    await client.DisconnectAsync();
                }
                catch (Exception ex)
                {
                    Log.Error(ex);
                }
            }
            catch (OperationCanceledException)
            {
                //Plugin unloading, Try to disconnect 
                try
                {
                    await client.DisconnectAsync();
                }
                catch(Exception ex)
                {
                    Log.Error(ex);
                }
            }
            catch(Exception ex)
            {
                Log.Warn("Lost connection to server {h}, {m}", server.ServerId, ex);
            }
            finally
            {
                //Remove server from active list, since its been disconnected
                RemoveServer(server);
                client.Dispose();
            }
        }

        protected override void ProcessHostCommand(string cmd)
        {
            Log.Debug(cmd);
        }


        #endregion
    }
}
