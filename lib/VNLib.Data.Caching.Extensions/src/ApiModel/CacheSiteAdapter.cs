﻿/*
* Copyright (c) 2024 Vaughn Nugent
* 
* Library: VNLib
* Package: VNLib.Data.Caching.Extensions
* File: CacheSiteAdapter.cs 
*
* CacheSiteAdapter.cs is part of VNLib.Data.Caching.Extensions which is part of the larger 
* VNLib collection of libraries and utilities.
*
* VNLib.Data.Caching.Extensions is free software: you can redistribute it and/or modify 
* it under the terms of the GNU Affero General Public License as 
* published by the Free Software Foundation, either version 3 of the
* License, or (at your option) any later version.
*
* VNLib.Data.Caching.Extensions is distributed in the hope that it will be useful,
* but WITHOUT ANY WARRANTY; without even the implied warranty of
* MERCHANTABILITY or FITNESS FOR A PARTICULAR PURPOSE. See the
* GNU Affero General Public License for more details.
*
* You should have received a copy of the GNU Affero General Public License
* along with this program.  If not, see https://www.gnu.org/licenses/.
*/

using System;
using System.Net;
using System.Security;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

using RestSharp;

using VNLib.Net.Rest.Client;
using VNLib.Net.Rest.Client.Construction;

namespace VNLib.Data.Caching.Extensions.ApiModel
{
    /// <summary>
    /// A site adapter for cache REST api requests
    /// </summary>
    internal sealed class CacheSiteAdapter : RestSiteAdapterBase
    {
        /*
         * Lazy to defer errors for debuggong
         */
        private static readonly Lazy<CacheSiteAdapter> _lazy = new(() => ConfigureAdapter(2));

        internal static CacheSiteAdapter Instance => _lazy.Value;

        private static CacheSiteAdapter ConfigureAdapter(int maxClients)
        {
            CacheSiteAdapter adapter = new(maxClients);
            //Configure the site endpoints
            adapter.BuildEndpoints(ServiceEndpoints.Definition);
            return adapter;
        }


        protected override RestClientPool Pool { get; }

        public CacheSiteAdapter(int maxClients)
        {
            //Configure connection pool
            Pool = new(maxClients, new RestClientOptions()
            {
                Timeout = TimeSpan.FromSeconds(value: 10),
                FollowRedirects = false,
                Encoding = Encoding.UTF8,
                AutomaticDecompression = DecompressionMethods.All,
                ThrowOnAnyError = true
            });
        }

        public override void OnResponse(RestResponse response)
        {
            switch(response.StatusCode)
            {
                case HttpStatusCode.OK:
                    break;
                case HttpStatusCode.Unauthorized:
                    throw new SecurityException("Unauthorized access to cache service");
                case HttpStatusCode.Forbidden:
                    throw new SecurityException("Forbidden access to cache service");
                case HttpStatusCode.NotFound:
                    throw new InvalidOperationException("Cache service not found");
                case HttpStatusCode.InternalServerError:
                    throw new InvalidOperationException("Cache service internal error");
                default:
                    throw new InvalidOperationException($"Cache service error: {response.StatusCode}");
            }
        }

        public override Task WaitAsync(CancellationToken cancellation = default)
        {
            return Task.CompletedTask;
        }
    }
}
