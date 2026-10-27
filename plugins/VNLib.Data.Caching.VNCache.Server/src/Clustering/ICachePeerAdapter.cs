/*
* Copyright (c) 2026 Vaughn Nugent
* 
* Library: VNLib
* Package: VNLib.Data.Caching.VNCache.Server
* File: ICachePeerAdapter.cs 
*
* ICachePeerAdapter.cs is part of VNLib.Data.Caching.VNCache.Server which is part of the larger 
* VNLib collection of libraries and utilities.
*
* VNLib.Data.Caching.VNCache.Server is free software: you can redistribute it and/or modify 
* it under the terms of the GNU Affero General Public License as 
* published by the Free Software Foundation, either version 3 of the
* License, or (at your option) any later version.
*
* VNLib.Data.Caching.VNCache.Server is distributed in the hope that it will be useful,
* but WITHOUT ANY WARRANTY; without even the implied warranty of
* MERCHANTABILITY or FITNESS FOR A PARTICULAR PURPOSE. See the
* GNU Affero General Public License for more details.
*
* You should have received a copy of the GNU Affero General Public License
* along with this program.  If not, see https://www.gnu.org/licenses/.
*/

using VNLib.Data.Caching.VNCache.Clustering;

namespace VNLib.Data.Caching.VNCache.Server.Clustering
{
    internal interface ICachePeerAdapter
    {
        /// <summary>
        /// Gets the peers that have been discovered but not yet connected to
        /// </summary>
        /// <returns>A collection of peers that have not been connected to yet</returns>
        CacheNodeAdvertisment[] GetNewPeers();

        /// <summary>
        /// Called when a peer has been connected to
        /// </summary>
        /// <param name="peer">The peer that has been connected</param>
        void OnPeerListenerAttached(CacheNodeAdvertisment peer);

        /// <summary>
        /// Called when a peer has been disconnected from
        /// </summary>
        /// <param name="peer">The disconnected peer</param>
        void OnPeerListenerDetatched(CacheNodeAdvertisment peer);
    }
}
