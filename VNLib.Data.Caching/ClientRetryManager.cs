/*
* Copyright (c) 2022 Vaughn Nugent
* 
* Library: VNLib
* Package: VNLib.Data.Caching
* File: ClientRetryManager.cs 
*
* ClientRetryManager.cs is part of VNLib.Data.Caching which is part of the larger 
* VNLib collection of libraries and utilities.
*
* VNLib.Data.Caching is free software: you can redistribute it and/or modify 
* it under the terms of the GNU Affero General Public License as 
* published by the Free Software Foundation, either version 3 of the
* License, or (at your option) any later version.
*
* VNLib.Data.Caching is distributed in the hope that it will be useful,
* but WITHOUT ANY WARRANTY; without even the implied warranty of
* MERCHANTABILITY or FITNESS FOR A PARTICULAR PURPOSE. See the
* GNU Affero General Public License for more details.
*
* You should have received a copy of the GNU Affero General Public License
* along with this program.  If not, see https://www.gnu.org/licenses/.
*/

using System;
using System.Threading.Tasks;
using System.Security.Cryptography;

using VNLib.Utils;
using VNLib.Net.Messaging.FBM.Client;

namespace VNLib.Data.Caching
{
    /// <summary>
    /// Manages a <see cref="FBMClientWorkerBase"/> reconnect policy
    /// </summary>
    public class ClientRetryManager<T> : VnDisposeable where T: IStatefulConnection
    {
        const int RetryRandMaxMsDelay = 1000;

        private readonly TimeSpan RetryDelay;
        private readonly T Client;
        private readonly Uri ServerUri;

        internal ClientRetryManager(T worker, TimeSpan delay, Uri serverUri)
        {
            this.Client = worker;
            this.RetryDelay = delay;
            this.ServerUri = serverUri;
            //Register disconnect listener
            worker.ConnectionClosed += Worker_Disconnected;
        }

        private void Worker_Disconnected(object? sender, EventArgs args)
        {
            //Exec retry on exit
            _ = RetryAsync().ConfigureAwait(false);
        }
       

        /// <summary>
        /// Raised before client is to be reconnected
        /// </summary>
        public event Action<T>? OnBeforeReconnect;
        /// <summary>
        /// Raised when the client fails to reconnect. Should return a value that instructs the 
        /// manager to reconnect
        /// </summary>
        public event Func<T, Exception, bool>? OnReconnectFailed;

        async Task RetryAsync()
        {
            
            //Begin random delay with retry ms
            int randomDelayMs = (int)RetryDelay.TotalMilliseconds;
            //random delay to add to prevent retry-storm
            randomDelayMs += RandomNumberGenerator.GetInt32(RetryRandMaxMsDelay);
            //Retry loop
            bool retry = true;
            while (retry)
            {
                try
                {
                    //Inform Listener for the retry
                    OnBeforeReconnect?.Invoke(Client);
                    //wait for delay before reconnecting
                    await Task.Delay(randomDelayMs);
                    //Reconnect async
                    await Client.ConnectAsync(ServerUri).ConfigureAwait(false);
                    break;
                }
                catch (Exception Ex)
                {
                    //Invoke error handler, may be null, incase exit
                    retry = OnReconnectFailed?.Invoke(Client, Ex) ?? false;
                }
            }
        }

        ///<inheritdoc/>
        protected override void Free()
        {
            //Unregister the event listener
            Client.ConnectionClosed -= Worker_Disconnected;
        }
    }
}
