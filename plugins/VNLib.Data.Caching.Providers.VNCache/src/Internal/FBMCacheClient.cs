/*
* Copyright (c) 2026 Vaughn Nugent
* 
* Library: VNLib
* Package: VNLib.Data.Caching.Providers.VNCache
* File: FBMCacheClient.cs 
*
* FBMCacheClient.cs is part of VNLib.Data.Caching.Providers.VNCache which is part of the larger 
* VNLib collection of libraries and utilities.
*
* VNLib.Data.Caching.Providers.VNCache is free software: you can redistribute it and/or modify 
* it under the terms of the GNU Affero General Public License as 
* published by the Free Software Foundation, either version 3 of the
* License, or (at your option) any later version.
*
* VNLib.Data.Caching.Providers.VNCache is distributed in the hope that it will be useful,
* but WITHOUT ANY WARRANTY; without even the implied warranty of
* MERCHANTABILITY or FITNESS FOR A PARTICULAR PURPOSE. See the
* GNU Affero General Public License for more details.
*
* You should have received a copy of the GNU Affero General Public License
* along with this program.  If not, see https://www.gnu.org/licenses/.
*/

using System;
using System.IO;
using System.Net.Http;
using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;
using System.Net.Sockets;
using System.Net.WebSockets;
using System.Security.Cryptography;

using VNLib.Utils.Memory;
using VNLib.Utils.Logging;
using VNLib.Net.Messaging.FBM.Client;
using VNLib.Data.Caching.Extensions;
using VNLib.Data.Caching.Extensions.Clustering;

using VNLib.Plugins;
using VNLib.Plugins.Extensions.Loading;
using VNLib.Plugins.Extensions.Loading.Events;

using VNLib.Data.Caching.Providers.VNCache.Clustering;

namespace VNLib.Data.Caching.Providers.VNCache.Internal
{

    /// <summary>
    /// A base class that manages 
    /// </summary>
    internal sealed class FBMCacheClient : VNCacheBase
    {
        private const string LOG_NAME = "FBMCache";
        private static readonly TimeSpan InitialDelay = TimeSpan.FromSeconds(10);

        private readonly VNRemoteCacheConfig _config;

        private bool _isConnected;
        private FBMClient? _client;

        /// <summary>
        /// The internal heap used for FBMClients
        /// </summary>
        public IUnmanagedHeap BufferHeap { get; } = MemoryUtil.Shared;

        /// <summary>
        /// Gets a value that determines if the client is currently connected to a server
        /// </summary>
        public override bool IsConnected => _isConnected;

        internal FBMCacheClient(VNRemoteCacheConfig config) : base(config)
        {
            Debug.Assert(config != null);
            _config = config;
        }

        private void ConfigureCluster(PluginBase? plugin, ref IClusterNodeIndex index, ref VNCacheClusterClient cluster)
        {
            //Init the client with default settings
            FBMClientConfig conf = FBMDataCacheExtensions.GetDefaultConfig(
                BufferHeap,
                maxMessageSize: (int)_config.MaxBlobSize,
                timeout: _config.RequestTimeout,
                debugLog: _config.ClientDebugLog
            );

            FBMClientFactory clientFactory = new(
                in conf,
                webSocketManager: new FBMFallbackClientWsFactory(),
                maxClients: 10
            );

            CacheClientConfiguration clusterConfig = new CacheClientConfiguration()
                .WithTls(_config.UseTls)
                .WithAuthenticator(_config.AuthManager)
                .WithInitialPeers(_config.GetInitialNodeUris());

            //See if were executing in the context of a plugin
            if (plugin is not null)
            {
                ILogProvider scoped = plugin.Log.CreateScope(LOG_NAME);

                //When in plugin context, we can use plugin local secrets and a log-based error handler
                clusterConfig
                    .WithErrorHandler(new DiscoveryErrHandler(scoped));
            }

            cluster = clusterConfig.ToClusterClient(clientFactory);

            index = ClusterNodeIndex.CreateIndex(cluster);
        }

        /// <summary>
        /// Waits for cluster discovery to complete and returns the next node to connect to.
        /// Master and follower instances share this path and differ only in failure handling.
        /// </summary>
        /// <param name="index">The shared cluster node index.</param>
        /// <param name="operationLog">A log provider to write connection and logging data to.</param>
        /// <param name="exitToken">A token that will gracefully stop a client connection when cancelled.</param>
        /// <returns>The next node to connect to, or null when no nodes are available.</returns>
        private static async Task<CacheNodeAdvertisment?> WaitForNextNodeAsync(
            IClusterNodeIndex index,
            ILogProvider operationLog,
            bool masterIndex,
            CancellationToken exitToken
        )
        {
            try
            {
                //Wait for a discovery to complete
                await index.WaitForDiscoveryAsync(exitToken);
            }
            catch (CacheDiscoveryFailureException cdfe) when (masterIndex)
            {
                operationLog.Error("Failed to discover nodes, will try again\n{err}", cdfe.Message);
                //Continue
            }
            // Rethrow when we're the master node
            catch when (masterIndex)
            {
                throw;
            }
            catch (Exception ex)
            {
                operationLog.Debug(ex, "Failed to wait for discovery");
                //Exception types from the other side so we can not really granularly handle
                //them, but master instance should so we just need to wait
            }

            //Get the next node to connect to
            return index.GetNextNode();
        }


        /// <summary>
        /// Begins the lifecycle of a cache cluster client by discovering cluster nodes
        /// choosing a node, and running a connection loop with the cluster. 
        /// <para>
        /// Unless running in a plugin context, you must call this function to begin the
        /// cache client. DO NOT call this function if running in plugin context, it will
        /// be scheduled in the background.
        /// </para>
        /// <para>
        /// Retries wait longer each time (with some randomness) once you configure the
        /// bounds below, otherwise we just wait a fixed delay like before. Only a
        /// connection that stays up resets the wait.
        /// </para>
        /// </summary>
        /// <param name="operationLog">A log provider to write connection and logging data to</param>
        /// <param name="exitToken">A token that will gracefully stop a client connection when cancelled</param>
        /// <returns>A task that represents this background operation</returns>
        public override async Task RunAsync(PluginBase? plugin, ILogProvider operationLog, CancellationToken exitToken)
        {
            IClusterNodeIndex index = null!;
            VNCacheClusterClient cluster = null!;
            CacheNodeAdvertisment? node = null;

            ConfigureCluster(plugin, ref index, ref cluster);

            //Set a default node delay if null
            TimeSpan initNodeDelay = _config.InitialNodeDelay.HasValue
                    ? TimeSpan.FromMilliseconds(_config.InitialNodeDelay.Value)
                    : InitialDelay;

            //Unset bounds default equal, which keeps the old fixed delay behavior
            ReconnectBackoff backoff = new(_config.ReconnectInitialDelay, _config.ReconnectMaxDelay);

            try
            {
                //Initial delay
                operationLog.Debug("Worker started, waiting for startup delay");
                await Task.Delay(initNodeDelay, exitToken);

                //See if the current client index is a master index
                if (index is IIntervalSchedulable masterIndex)
                {
                    if (plugin is not null)
                    {
                        ILogProvider scoped = plugin.Log.CreateScope(LOG_NAME);

                        //Schedule discovery interval on the plugin scheduler
                        plugin.ScheduleInterval(masterIndex, _config.DiscoveryInterval);

                        _ = plugin.Tasks().ObserveWork(
                            asyncTask: () => masterIndex.OnIntervalAsync(scoped, plugin.UnloadToken),
                            delayMs: (int)initNodeDelay.TotalMilliseconds
                        );
                    }
                    else
                    {
                        //Trigger initial discovery manually
                        _ = masterIndex.OnIntervalAsync(operationLog, exitToken);
                    }
                }

                while (true)
                {
                    /*
                     * The cache node index is shared across plugin boundaries. If the current
                     * instance is holding the master index, it will be schedulable, and 
                     * can be manually invoked if no nodes are found
                     */
                  
                    CacheNodeAdvertisment? next = await WaitForNextNodeAsync(
                        index, 
                        operationLog, 
                        masterIndex: index is IIntervalSchedulable, 
                        exitToken
                    ).ConfigureAwait(false);

                    if (next is null)
                    {
                        //Every failure makes us wait a little longer, with some randomness thrown in,
                        //so a fleet of clients doesn't all hammer the server at the same moment
                        TimeSpan delay = backoff.NextDelay();

                        operationLog.Warn("No nodes available to connect to, trying again in {delay}", delay);
                        await Task.Delay(delay, exitToken);

                        //Run another manual discovery if the interval is greater than the delay
                        if (index is IIntervalSchedulable sch && _config.DiscoveryInterval > delay)
                        {
                            operationLog.Debug("Forcing a manual discovery");

                            if (plugin is not null)
                            {
                                //Make sure a failed discovery gets logged instead of disappearing on us
                                _ = plugin.Tasks().ObserveWork(
                                    asyncTask: () => sch.OnIntervalAsync(operationLog, exitToken),
                                    delayMs: 0
                                );
                            }
                            else
                            {
                                //No plugin scheduler exists outside plugin context
                                _ = sch.OnIntervalAsync(operationLog, exitToken);
                            }
                        }

                        continue;
                    }

                    // Ready to connect
                    node = next;

                    try
                    {
                        operationLog.Debug("Connecting to {node}", node);

                        //Connect to the node and save new client
                        _client = await cluster.ConnectToCacheAsync(node, exitToken);

                        if (operationLog.IsEnabled(LogLevel.Debug))
                        {
                            operationLog.Debug("Connected server: {s}", node);
                        }
                        else
                        {
                            operationLog.Information("Successfully connected to cache node");
                        }

                        //Set connection status flag
                        _isConnected = true;

                        //Remember when we connected, so we can tell a good connection from one that's flapping
                        long startTime = Stopwatch.GetTimestamp();

                        //Wait for disconnect
                        await _client.WaitForExitAsync(exitToken);

                        //Only a connection that actually stayed up earns a clean slate
                        backoff.ResetIfHealthy(Stopwatch.GetElapsedTime(startTime));

                        operationLog.Information("Cache server disconnected");
                    }
                    catch (TimeoutException)
                    {
                        operationLog.Warn("Failed to establish a websocket connection to cache server within the timeout period");

                        //Don't spin in a tight loop against a server that accepts then times out
                        await Task.Delay(backoff.NextDelay(), exitToken);
                    }
                    catch (WebSocketException wse)
                    {
                        operationLog.Warn("Failed to establish a websocket connection to cache server {reason}", wse.Message);
                        operationLog.Verbose("Stack trace: {re}", wse);
                    }
                    //SEs may be raised when the server is not available
                    catch (HttpRequestException he) when (he.InnerException is SocketException se)
                    {
                        operationLog.Debug("Failed to connect to random cache server because a TCP connection could not be established");
                        operationLog.Verbose("Stack trace: {re}", se);
                        await Task.Delay(backoff.NextDelay(), exitToken);
                    }
                    catch (HttpRequestException he) when (he.InnerException is IOException ioe && ioe.InnerException is SocketException se)
                    {
                        operationLog.Debug("Failed to connect to random cache server because a TCP connection could not be established");
                        operationLog.Verbose("Stack trace: {re}", se);
                        await Task.Delay(backoff.NextDelay(), exitToken);
                    }
                    catch (HttpRequestException he) when (he.StatusCode.HasValue)
                    {
                        operationLog.Warn("Failed to negotiate with cache server {reason}", he.Message);
                        operationLog.Verbose("Stack trace: {re}", he);
                        await Task.Delay(backoff.NextDelay(), exitToken);
                    }
                    finally
                    {
                        _isConnected = false;

                        //Cleanup client
                        _client?.Dispose();
                    }

                    //Loop again
                }
            }
            catch (OperationCanceledException)
            {
                //Normal exit from listening loop
            }
            catch (FBMServerNegiationException fne)
            {
                operationLog.Error("Failed to negotiate connection with cache server. Please check your configuration\n {reason}", fne.Message);
            }
            catch (Exception ex)
            {
                operationLog.Error(ex, "Unhandled exception occurred in background cache client listening task");
            }

            operationLog.Information("Cache client exited");
        }


        ///<inheritdoc/>
        public override Task<bool> DeleteAsync(string key, CancellationToken cancellation)
        {
            return !IsConnected
                ? Task.FromException<bool>(new InvalidOperationException("The underlying client is not connected to a cache node"))
                : _client!.DeleteObjectAsync(key, cancellation);
        }

        ///<inheritdoc/>
        public override Task<T> GetAsync<T>(string key, ICacheObjectDeserializer deserializer, CancellationToken cancellation)
        {
            return !IsConnected
                ? Task.FromException<T>(new InvalidOperationException("The underlying client is not connected to a cache node"))
                : _client!.GetObjectAsync<T>(key, deserializer, cancellation);
        }

        ///<inheritdoc/>
        public override Task AddOrUpdateAsync<T>(string key, string? newKey, T value, ICacheObjectSerializer serializer, CancellationToken cancellation)
        {
            return !IsConnected
                ? Task.FromException(new InvalidOperationException("The underlying client is not connected to a cache node"))
                : _client!.AddOrUpdateObjectAsync(key, newKey, value, serializer, cancellation);
        }

        ///<inheritdoc/>
        public override Task GetAsync<T>(string key, ObjectDataSet<T> callback, T state, CancellationToken cancellation)
        {
            return !IsConnected
                ? Task.FromException(new InvalidOperationException("The underlying client is not connected to a cache node"))
                : _client!.GetObjectAsync(key, callback, state, cancellation);
        }

        ///<inheritdoc/>
        public override Task AddOrUpdateAsync<T>(string key, string? newKey, ObjectDataGet<T> callback, T state, CancellationToken cancellation)
        {
            return !IsConnected
                ? Task.FromException(new InvalidOperationException("The underlying client is not connected to a cache node"))
                : _client!.AddOrUpdateObjectAsync(key, newKey, callback, state, cancellation);
        }

        ///<inheritdoc/>
        public override object GetUnderlyingStore() => _client ?? throw new InvalidOperationException("The client is not currently connected");


        /// <summary>
        /// Figures out how long to wait before trying the server again. Waits get
        /// longer after every failure so we don't hammer a dead server, with some
        /// randomness so clients don't all retry at once. Leave the bounds unset
        /// and we just wait the old fixed delay.
        /// </summary>
        private sealed class ReconnectBackoff
        {
            private readonly TimeSpan _initial;
            private readonly TimeSpan _max;

            private uint _failureCount;
            private TimeSpan _lastDelay = TimeSpan.Zero;

            /// <summary>
            /// Initializes a new instance of the <see cref="ReconnectBackoff"/> class.
            /// </summary>
            /// <param name="initial">The delay for the first attempt after a healthy period.</param>
            /// <param name="max">The upper bound the computed delay never exceeds.</param>
            public ReconnectBackoff(TimeSpan initial, TimeSpan max)
            {
                ArgumentOutOfRangeException.ThrowIfNegativeOrZero(initial.Ticks);
                ArgumentOutOfRangeException.ThrowIfNegativeOrZero(max.Ticks);

                _initial = initial;
                _max = max;
            }

            /// <summary>
            /// Works out the next wait and counts another failure.
            /// </summary>
            /// <returns>The delay to wait before trying again.</returns>
            public TimeSpan NextDelay()
            {
                // Range was not configured, keep the old fixed wait
                if (_max <= _initial)
                {
                    _lastDelay = _initial;
                    return _initial;
                }

                // Double the wait every time, but never past the max. The clamp
                // keeps the math from overflowing on a really long outage.
                long grownTicks = _initial.Ticks * (1L << (int)Math.Min(_failureCount, 20u));
                long cappedTicks = Math.Min(grownTicks, _max.Ticks);

                _failureCount++;
                _lastDelay = TimeSpan.FromTicks(cappedTicks);

                // Pick a random wait under the cap, that's what keeps clients from
                // retrying in lockstep. Capped to max integer
                int upperBoundMs = (int)Math.Clamp(_lastDelay.TotalMilliseconds, min: 1L, int.MaxValue);
                
                return TimeSpan.FromMilliseconds(RandomNumberGenerator.GetInt32(0, upperBoundMs));
            }

            /// <summary>
            /// Forgets past failures, but only if the connection actually stayed up
            /// for a while. Brief connect-then-drop cycles keep backing off.
            /// </summary>
            /// <param name="connectedFor">How long we stayed connected.</param>
            public void ResetIfHealthy(TimeSpan connectedFor)
            {
                if (connectedFor >= _lastDelay)
                {
                    _failureCount = 0;
                }
            }
        }

        private sealed record class DiscoveryErrHandler(ILogProvider Logger) : ICacheDiscoveryErrorHandler
        {
            public void OnDiscoveryError(CacheNodeAdvertisment errorNode, Exception ex)
                => OnDiscoveryError(ex, errorNode, address: null);

            public void OnDiscoveryError(Uri errorAddress, Exception ex)
                => OnDiscoveryError(ex, errorNode: null, errorAddress);

            public void OnDiscoveryError(Exception ex, CacheNodeAdvertisment? errorNode, Uri? address)
            {
                string node = errorNode?.NodeId ?? address?.ToString() ?? "unknown";

                if (ex is HttpRequestException he)
                {
                    if (he.InnerException is SocketException se)
                    {
                        LogErrorException(se);
                        return;
                    }

                    LogErrorException(he);
                    return;
                }

                LogErrorException(ex);
                return;

                void LogErrorException(Exception ex)
                {
                    if (Logger.IsEnabled(LogLevel.Debug))
                    {
                        Logger.Error("Failed to discover nodes from server {s} cause:\n{err}", node, ex);
                    }
                    else
                    {
                        Logger.Error("Failed to discover nodes from server {s} cause:\n{err}", node, ex.Message);
                    }
                }
            }
        }
    }
}
