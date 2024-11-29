/*
* Copyright (c) 2024 Vaughn Nugent
* 
* Library: VNLib
* Package: ObjectCacheServer
* File: ConnectEndpoint.cs 
*
* ConnectEndpoint.cs is part of ObjectCacheServer which is part of the larger 
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
using System.Net;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using System.Collections.Generic;
using System.Collections.Frozen;
using System.Text.Json.Serialization;

using VNLib.Hashing.IdentityUtility;
using VNLib.Net.Http;
using VNLib.Utils.Logging;
using VNLib.Plugins;
using VNLib.Plugins.Essentials;
using VNLib.Plugins.Essentials.Endpoints;
using VNLib.Plugins.Essentials.Extensions;
using VNLib.Plugins.Extensions.Loading;
using VNLib.Plugins.Extensions.Loading.Routing.Mvc;


namespace VNLib.Data.Caching.ObjectCache.Server.Rpc
{
    internal interface ICacheClientAuthManager
    {
        ValueTask<AuthResult> IsClientAuthorized(HttpEntity entity);

        ValueTask<AuthResult> IsClientAuthorized(JsonWebToken jwt);
    }

    internal class GossipAdvertisement
    {
        [JsonPropertyName("node_id")]
        public string Id { get; init; }

        [JsonPropertyName("addresses")]
        public string[] ConnectAddress { get; init; } = [];

        [JsonPropertyName("public_key")]
        public string? PublicKey { get; init; }

        [JsonPropertyName("ttl")]
        public DateTime Expires { get; init; }
    }

    internal interface IGossipManager
    {
        void AddPeers(IEnumerable<GossipAdvertisement> peers);

        void RemovePeers(IEnumerable<GossipAdvertisement> peers);

        IReadOnlyCollection<GossipAdvertisement> GetPeers();
    }

    readonly record struct AuthResult(bool Authorized, bool IsPeer);

    sealed class GossipRpcController(PluginBase plugin) : ICacheRpcController
    {
        public IEnumerable<ICacheRpcMethod> GetMethods()
        {
            ObjectCacheSystemState sysState = plugin.GetOrCreateSingleton<ObjectCacheSystemState>();

            return [ new GetNodesMethod(sysState) ];
        }


        private sealed class GetNodesMethod(ObjectCacheSystemState state) : ICacheRpcMethod
        {
            private readonly ICacheClientAuthManager _authMan;
            private readonly IGossipManager _gossipMan;

            ///<inheritdoc/>
            public string Method { get; } = "gossip.nodes.get";

            ///<inheritdoc/>
            public async Task<CacheRpcResult> OnRequestAsync(HttpEntity entity, JsonElement request)
            {
                //Ensure the client is authorized to access this method
                AuthResult res = await _authMan.IsClientAuthorized(entity);

                if (!res.Authorized)
                {
                    return new CacheRpcResult(401, null);
                }

                //Get the nodes from the gossip manager
                IReadOnlyCollection<GossipAdvertisement> nodes = _gossipMan.GetPeers();

                return new CacheRpcResult(
                    ErrorCode: 200, 
                    Result: new GetNodeResponse { Nodes = nodes }
                );
            }

            private sealed class GetNodeResponse
            {
                [JsonPropertyName("nodes")]
                public required IReadOnlyCollection<GossipAdvertisement> Nodes { get; init; }
            }
        }
    }

    [ConfigurationName("rpc")]
    internal sealed class CacheRpcController : IHttpController
    {
        private static readonly JsonDocument EmptyDataRequest = JsonDocument.Parse("{}");

        private readonly FrozenDictionary<string, ICacheRpcMethod> _methods;

        public CacheRpcController(PluginBase plugin, IConfigScope config)
        {
            CacheRpcMethodConfig[] endpoints = config.GetRequiredProperty<CacheRpcMethodConfig[]>("methods");

            _methods = endpoints
                .Where(static p => p.Enabled)   //Only load enabled processor
                .Select(c => LoadControllerFromConfig(plugin, c))
                .Where(static c => c != null)   //Remove null processors
                .SelectMany(static s => s!.GetMethods())
                .ToFrozenDictionary(
                    keySelector: static m => m.Method,
                    StringComparer.OrdinalIgnoreCase
                );
        }

        private static ICacheRpcController? LoadControllerFromConfig(PluginBase plugin, CacheRpcMethodConfig proc)
        {
            switch (proc.Type)
            {
                //Load an external assembly for the processor
                case "external":
                    plugin.Log.Debug("Loading external cache rpc method from assembly {asm}", proc.ExernAssemblyPath);

                    return plugin.CreateServiceExternal<ICacheRpcController>(proc.ExernAssemblyPath!);

                case "gossip":
                    return plugin.CreateService<GossipRpcController>();

                default:
                    plugin.Log.Warn("Unknown rpc processor type '{type}', ignoring processor", proc.Type);
                    return null;
            }
        }


        ///<inheritdoc/>
        public ProtectionSettings GetProtectionSettings()
        {
            return new ProtectionSettings
            {
                DisableRefererMatch = true,
                DisableSessionsRequired = true
            };
        }


        [HttpStaticRoute("{{rpc_endpoint}}", HttpMethod.POST)]
        public async ValueTask<VfReturnType> OnRpcAsync(HttpEntity entity)
        {

            //Client must accept json response messages
            if (!entity.Server.Accepts(ContentType.Json))
            {
                return ResourceEndpointBase.VirtualClose(entity, HttpStatusCode.NotAcceptable);
            }

            RpcResponse response = new();

            //Ensure the client sent a request messages and it's a json message
            if (entity.Files.Count == 0)
            {
                response.Error = "No files were uploaded";
                return Error(entity, HttpStatusCode.BadRequest, response);
            }
            if (entity.Files[0].ContentType != ContentType.Json)
            {
                response.Error = "Invalid content type. This endpoint only accepts JSON encoded messages";
                return Error(entity, HttpStatusCode.BadRequest, response);
            }

            //Get the request as a json document
            using JsonDocument request = (await entity.GetJsonFromFileAsync())!;

            if (!request.RootElement.TryGetProperty("method", out JsonElement method)
                && method.ValueKind == JsonValueKind.String)
            {
                response.Error = "No method was specified in the request";
                return Error(entity, HttpStatusCode.BadRequest, response);
            }

            string methodName = method.GetString()!;
            if (!_methods.TryGetValue(methodName, out ICacheRpcMethod? handler))
            {
                response.Error = "Method not found";
                return Error(entity, HttpStatusCode.NotFound, response);
            }

            //Try to get the request argument data object, otherwise use an empty object
            if (!request.RootElement.TryGetProperty("data", out JsonElement requestData))
            {
                requestData = EmptyDataRequest.RootElement;
            }

            CacheRpcResult rpcResult = await handler.OnRequestAsync(entity, requestData);

            response.Result = rpcResult.Result;

            if (rpcResult.ErrorCode != 0)
            {
                response.Error = "An error occured while processing the request";
                return Error(entity, HttpStatusCode.InternalServerError, response);
            }

            return Okay(entity, response);
        }

        private static VfReturnType Okay(HttpEntity entity, RpcResponse response)
        {
            return Error(entity, HttpStatusCode.OK, response);
        }

        private static VfReturnType Error(HttpEntity entity, HttpStatusCode code, RpcResponse response)
        {
            response.Code = (int)code;
            return ResourceEndpointBase.VirtualCloseJson(entity, response, code);
        }

        sealed class RpcResponse
        {
            [JsonPropertyName("code")]
            public int Code { get; set; }

            [JsonPropertyName("result")]
            public object? Result { get; set; }

            [JsonPropertyName("error")]
            public string? Error { get; set; }
        }


        private sealed class CacheRpcMethodConfig
        {
            [JsonPropertyName("enabled")]
            public bool Enabled { get; set; } = true;   //If the user defined a config but no enabled flag, assume they want it enabled

            /*
             * Define the processor type, an internal processor (like totp) or 
             * an external processor loaded in via and external .NET assembly
             */
            [JsonPropertyName("type")]
            public string? Type { get; set; }

            /*
             * Dll asset file path to load the external assembly from
             * if the processor type is set to "external"
             */
            [JsonPropertyName("assembly_path")]
            public string? ExernAssemblyPath { get; set; }
        }
    }
}
