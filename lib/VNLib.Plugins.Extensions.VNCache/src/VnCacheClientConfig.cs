﻿/*
* Copyright (c) 2023 Vaughn Nugent
* 
* Library: VNLib
* Package: VNLib.Plugins.Extensions.VNCache
* File: VnCacheClientConfig.cs 
*
* VnCacheClientConfig.cs is part of VNLib.Plugins.Extensions.VNCache which is part of the larger 
* VNLib collection of libraries and utilities.
*
* VNLib.Plugins.Extensions.VNCache is free software: you can redistribute it and/or modify 
* it under the terms of the GNU Affero General Public License as 
* published by the Free Software Foundation, either version 3 of the
* License, or (at your option) any later version.
*
* VNLib.Plugins.Extensions.VNCache is distributed in the hope that it will be useful,
* but WITHOUT ANY WARRANTY; without even the implied warranty of
* MERCHANTABILITY or FITNESS FOR A PARTICULAR PURPOSE. See the
* GNU Affero General Public License for more details.
*
* You should have received a copy of the GNU Affero General Public License
* along with this program.  If not, see https://www.gnu.org/licenses/.
*/

using System;
using System.Text.Json.Serialization;

using VNLib.Plugins.Extensions.Loading;

namespace VNLib.Plugins.Extensions.VNCache
{
    /// <summary>
    /// Represents a remote VNCache client configuration
    /// </summary>
    public class VnCacheClientConfig : IOnConfigValidation
    {
        /// <summary>
        /// The maximum size (in bytes) of messages sent to the 
        /// cache server. This value will be negotiated with the server
        /// during a connection upgrade
        /// </summary>
        [JsonPropertyName("max_message_size")]
        public int? MaxMessageSize { get; set; }

        /// <summary>
        /// The broker server address
        /// </summary>
        [JsonPropertyName("broker_address")]
        public string? BrokerAddress { get; set; }

        /// <summary>
        /// The time (in seconds) to randomly delay polling the broker server
        /// for available servers
        /// </summary>
        [JsonPropertyName("retry_interval_sec")]
        public int? RetryIntervalSeconds { get; set; }

        /// <summary>
        /// The maximum time (in seconds) for FBM cache operations are allowed
        /// to take before timing out. 
        /// </summary>
        /// <remarks>
        /// NOTE: You should set this value to something reasonable as FBM messages can 
        /// be lost and cause deadlocks if your cache implementation does not rely on
        /// CancellationTokens 
        /// </remarks>
        [JsonPropertyName("request_timeout_sec")]
        public int? RequestTimeoutSeconds { get; set; }

        /// <summary>
        /// Retry interval in a timespan
        /// </summary>
        internal TimeSpan RetryInterval => TimeSpan.FromSeconds(RetryIntervalSeconds!.Value);

        /// <summary>
        /// FBM Request timeout
        /// </summary>
        internal TimeSpan RequestTimeout => TimeSpan.FromSeconds(RequestTimeoutSeconds!.Value);

        void IOnConfigValidation.Validate()
        {
            if (!MaxMessageSize.HasValue || MaxMessageSize.Value < 1)
            {
                throw new ArgumentException("Your maxium message size should be a reasonable value greater than 0", "max_message_size");
            }

            if (!RetryIntervalSeconds.HasValue || RetryIntervalSeconds.Value < 1)
            {
                throw new ArgumentException("You must specify a retry interval period greater than 0", "retry_interval_sec");
            }

            //Allow a 0 timeout to disable timeouts, not recommended, but allowed
            if(!RequestTimeoutSeconds.HasValue || RequestTimeoutSeconds.Value < 0)
            {
                throw new ArgumentException("You must specify a positive integer FBM message timoeut", "request_timeout_sec");
            }

            if(!Uri.TryCreate(BrokerAddress, UriKind.RelativeOrAbsolute, out _))
            {
                throw new ArgumentException("You must specify a valid HTTP uri broker address", "broker_address");
            }
        }
    }
}