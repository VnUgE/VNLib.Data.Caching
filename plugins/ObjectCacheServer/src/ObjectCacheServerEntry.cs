/*
* Copyright (c) 2022 Vaughn Nugent
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

using VNLib.Utils.Memory;
using VNLib.Utils.Logging;
using VNLib.Utils.Extensions;
using VNLib.Hashing;
using VNLib.Hashing.IdentityUtility;
using VNLib.Data.Caching;
using VNLib.Data.Caching.Extensions;
using VNLib.Data.Caching.ObjectCache;
using static VNLib.Data.Caching.Constants;
using VNLib.Net.Messaging.FBM;
using VNLib.Net.Messaging.FBM.Client;
using VNLib.Plugins.Cache.Broker.Endpoints;
using VNLib.Plugins.Extensions.Loading;
using VNLib.Plugins.Extensions.Loading.Routing;
using VNLib.Plugins.Essentials.Sessions.Server.Endpoints;


namespace VNLib.Plugins.Essentials.Sessions.Server
{
    public sealed class ObjectCacheServerEntry : PluginBase
    {
        public override string PluginName => "ObjectCache.Service";

        private string? BrokerHeartBeatToken;

        private readonly object ServerLock = new();
        private readonly HashSet<ActiveServer> ListeningServers = new();


        private void RemoveServer(ActiveServer server)
        {
            lock (ServerLock)
            {
                ListeningServers.Remove(server);
            }
        }

        protected override void OnLoad()
        {
            //Create default heap
            IUnmangedHeap CacheHeap = MemoryUtil.InitializeNewHeapForProcess();
            try
            {
                IReadOnlyDictionary<string, JsonElement> clusterConf = this.GetConfig("cluster");

                string brokerAddress = clusterConf["broker_address"].GetString() ?? throw new KeyNotFoundException("Missing required key 'broker_address' for config 'cluster'");

                string swapDir = PluginConfig.GetProperty("swap_dir").GetString() ?? throw new KeyNotFoundException("Missing required key 'swap_dir' for config");
                int cacheSize = PluginConfig.GetProperty("max_cache").GetInt32();
                string connectPath = PluginConfig.GetProperty("connect_path").GetString() ?? throw new KeyNotFoundException("Missing required element 'connect_path' for config 'cluster'");
                //TimeSpan cleanupInterval = PluginConfig.GetProperty("cleanup_interval_sec").GetTimeSpan(TimeParseType.Seconds);
                //TimeSpan validFor = PluginConfig.GetProperty("valid_for_sec").GetTimeSpan(TimeParseType.Seconds);
                int maxMessageSize = PluginConfig.GetProperty("max_blob_size").GetInt32();
             
                //Init dir
                DirectoryInfo dir = new(swapDir);
                dir.Create();
                //Init cache listener, single threaded reader
                ObjectCacheStore CacheListener = new(dir, cacheSize, Log, CacheHeap, true);
                
                //Init connect endpoint
                {
                    //Init connect endpoint
                    ConnectEndpoint endpoint = new(connectPath, CacheListener, this);
                    Route(endpoint);
                }
                
                //Setup broker and regitration
                {
                    //init mre to pass the broker heartbeat signal to the registration worker
                    ManualResetEvent mre = new(false);
                    //Route the broker endpoint
                    BrokerHeartBeat brokerEp = new(() => BrokerHeartBeatToken!, mre, new Uri(brokerAddress), this);
                    Route(brokerEp);
                    
                    //start registration 
                    _ = this.DeferTask(() => RegisterServerAsync(mre), 200);
                }
                
                //Setup cluster worker
                {
                    //Get pre-configured fbm client config for caching
                    FBMClientConfig conf = FBMDataCacheExtensions.GetDefaultConfig(CacheHeap, maxMessageSize, this.IsDebug() ? Log : null);

                    //Start Client runner
                    _ = this.DeferTask(() => RunClientAsync(CacheListener, new Uri(brokerAddress), conf), 300);
                }
                
                //Load a cache broker to the current server if the config is defined
                {
                    if(this.HasConfigForType<BrokerRegistrationEndpoint>())
                    {
                        this.Route<BrokerRegistrationEndpoint>();
                    }
                }
                
                void Cleanup()
                {
                    CacheHeap.Dispose();
                    CacheListener.Dispose();
                }
                
                //Regsiter cleanup
                _ = UnloadToken.RegisterUnobserved(Cleanup);

                Log.Information("Plugin loaded");
            }
            catch (KeyNotFoundException kne)
            {
                CacheHeap.Dispose();
                Log.Error("Missing required configuration variables {m}", kne.Message);
            }
            catch
            {
                CacheHeap.Dispose();
                throw;
            }
        }

        protected override void OnUnLoad()
        {
            Log.Information("Plugin unloaded");
        }

        #region Registration

        private async Task RegisterServerAsync(ManualResetEvent keepaliveWait)
        {
            try
            {
                //Get the broker config element
                IReadOnlyDictionary<string, JsonElement> clusterConfig = this.GetConfig("cluster");                
                
                //Server id is just dns name for now
                string serverId = Dns.GetHostName();
                int heartBeatDelayMs = clusterConfig["heartbeat_timeout_sec"].GetInt32() * 1000;

                string? connectPath = PluginConfig.GetProperty("connect_path").GetString();
                
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
                            if (!keepaliveWait.WaitOne(0))
                            {
                                //server miseed a keepalive event, time to break the loop and retry
                                Log.Debug("Broker missed a heartbeat request, attempting to re-register");
                                break;
                            }
                            //Reset the msr
                            keepaliveWait.Reset();
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
                keepaliveWait.Dispose();
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
        private async Task RunClientAsync(ObjectCacheStore cacheStore, Uri brokerAddress, FBMClientConfig clientConf)
        {
            TimeSpan noServerDelay = TimeSpan.FromSeconds(10);
            string nodeId = Dns.GetHostName();
            ListServerRequest listRequest = new(brokerAddress);
            try
            {
                //Get the broker config element
                IReadOnlyDictionary<string, JsonElement> clusterConf = this.GetConfig("cluster");
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
                                _ = this.DeferTask(() => RunSyncTaskAsync(server, cacheStore, clientConf, nodeId));
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

        private async Task RunSyncTaskAsync(ActiveServer server, ObjectCacheStore cacheStore, FBMClientConfig conf, string nodeId)
        {
            //Setup client 
            FBMClient client = new(conf);
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
                        string status = response.Headers.First(static s => s.Key == HeaderCommand.Status).Value.ToString();
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
