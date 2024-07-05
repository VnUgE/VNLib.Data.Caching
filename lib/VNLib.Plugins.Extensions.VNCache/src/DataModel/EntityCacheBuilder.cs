/*
* Copyright (c) 2024 Vaughn Nugent
* 
* Library: VNLib
* Package: VNLib.Plugins.Extensions.VNCache
* File: EntityCacheBuilder.cs 
*
* EntityCacheBuilder.cs is part of VNLib.Plugins.Extensions.VNCache 
* which is part of the larger VNLib collection of libraries and utilities.
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
using System.Threading.Tasks;

using VNLib.Utils.Logging;

namespace VNLib.Plugins.Extensions.VNCache.DataModel
{
    /// <summary>
    /// Represents a cache that can store entities by their unique key
    /// </summary>
    /// <typeparam name="TEntity"></typeparam>
    /// <param name="cache"></param>
    public sealed class EntityCacheBuilder<TEntity>(IEntityCache<TEntity> cache)
        where TEntity : class
    {
        ICacheExpirationPolicy<TEntity>? _expPolicy;
        ICacheTaskPolicy? _taskPolicy;

        public EntityCacheBuilder<TEntity> WithExpirationPoicy(ICacheExpirationPolicy<TEntity> expiration)
        {
            _expPolicy = expiration;
            return this;
        }

        public EntityCacheBuilder<TEntity> WithInfiniteExpiration()
        {
            _expPolicy = new InfiniteCacheExpirationPolicy<TEntity>();
            return this;
        }

        public EntityCacheBuilder<TEntity> WithTaskPolicy(ICacheTaskPolicy taskPolicy)
        {
            _taskPolicy = taskPolicy;
            return this;
        }

        public EntityCacheBuilder<TEntity> WithWriteBackPolicy(Action<Task> onFaulted)
        {
            _taskPolicy = new WriteBackCachePolicy(onFaulted);
            return this;
        }

        public EntityCacheBuilder<TEntity> WithWriteBackPolicy(ILogProvider logger, LogLevel logLevel = LogLevel.Warning)
        {
            _taskPolicy = new WriteBackCachePolicy((t) =>
            {
                logger.Write(logLevel, t.Exception!, "Background task operation failed with exception: ");
            });

            return this;
        }

        public EntityCacheBuilder<TEntity> WithWriteThoughPolicy()
        {
            _taskPolicy = WriteThroughCachePolicy.Instance;
            return this;
        }
      

        public EntityResultCache<TEntity> Build()
        {
            ArgumentNullException.ThrowIfNull(_expPolicy, "Expiration");
            ArgumentNullException.ThrowIfNull(cache);
            ArgumentNullException.ThrowIfNull(_taskPolicy, "TaskPolicy");

            return new EntityResultCache<TEntity>(cache, _taskPolicy, _expPolicy);
        }
    }
   
}
