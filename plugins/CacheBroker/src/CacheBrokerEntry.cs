/*
* Copyright (c) 2022 Vaughn Nugent
* 
* Library: VNLib
* Package: CacheBroker
* File: CacheBrokerEntry.cs 
*
* CacheBrokerEntry.cs is part of CacheBroker which is part of the larger 
* VNLib collection of libraries and utilities.
*
* CacheBroker is free software: you can redistribute it and/or modify 
* it under the terms of the GNU Affero General Public License as 
* published by the Free Software Foundation, either version 3 of the
* License, or (at your option) any later version.
*
* CacheBroker is distributed in the hope that it will be useful,
* but WITHOUT ANY WARRANTY; without even the implied warranty of
* MERCHANTABILITY or FITNESS FOR A PARTICULAR PURPOSE. See the
* GNU Affero General Public License for more details.
*
* You should have received a copy of the GNU Affero General Public License
* along with this program.  If not, see https://www.gnu.org/licenses/.
*/

using System;
using System.Collections.Generic;

using VNLib.Utils.Logging;
using VNLib.Plugins.Cache.Broker.Endpoints;
using VNLib.Plugins.Extensions.Loading.Routing;

namespace VNLib.Plugins.Cache.Broker
{
    public sealed class CacheBrokerEntry : PluginBase
    {
        public override string PluginName => "ObjectCache.Broker";

        protected override void OnLoad()
        {
            try
            {
                this.Route<BrokerRegistrationEndpoint>();

                Log.Information("Plugin loaded");
            }
            catch (KeyNotFoundException knf)
            {
                Log.Error("Required configuration keys were not found {mess}", knf.Message);
            }
        }

        protected override void OnUnLoad()
        {
            Log.Debug("Plugin unloaded");
        }

        protected override void ProcessHostCommand(string cmd)
        {
            throw new NotImplementedException();
        }
    }
}
