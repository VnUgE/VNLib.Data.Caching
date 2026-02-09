/*
* Copyright (c) 2026 Vaughn Nugent
* 
* Library: VNLib
* Package: VNLib.Data.Caching.IntegrationTests
* File: SerilogLogger.cs 
*
* SerilogLogger.cs is part of VNLib.Data.Caching.IntegrationTests which is part of the larger 
* VNLib collection of libraries and utilities.
*
* VNLib.Data.Caching.IntegrationTests is free software: you can redistribute it and/or modify 
* it under the terms of the GNU Affero General Public License as 
* published by the Free Software Foundation, either version 3 of the
* License, or (at your option) any later version.
*
* VNLib.Data.Caching.IntegrationTests is distributed in the hope that it will be useful,
* but WITHOUT ANY WARRANTY; without even the implied warranty of
* MERCHANTABILITY or FITNESS FOR A PARTICULAR PURPOSE. See the
* GNU Affero General Public License for more details.
*
* You should have received a copy of the GNU Affero General Public License
* along with this program.  If not, see https://www.gnu.org/licenses/.
*/

using Serilog;
using Serilog.Core;
using Serilog.Events;

using System;

using VNLib.Utils.Logging;

namespace VNLib.Data.Caching.IntegrationTests.Tests.Static
{
    internal sealed class SerilogLogger : ILogProvider
    {
        private readonly Logger _log;

        public SerilogLogger()
        {
            LoggerConfiguration lc = new();

            lc.WriteTo.Console();
            lc.MinimumLevel.Verbose();

            _log = lc.CreateLogger();
        }

        public void Flush()
        { }

        public object GetLogProvider() => _log;

        public bool IsEnabled(LogLevel level) => true;

        public void Write(LogLevel level, string value)
            => _log.Write((LogEventLevel)level, value);

        public void Write(LogLevel level, Exception exception, string value = "")
            => _log.Write((LogEventLevel)level, exception, value);

        public void Write(LogLevel level, string value, params object?[] args)
            => _log.Write((LogEventLevel)level, value, args);

        public void Write(LogLevel level, string value, params ValueType[] args)
            => _log.Write((LogEventLevel)level, value, args);
    }
}