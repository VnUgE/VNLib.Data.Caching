/*
* Copyright (c) 2022 Vaughn Nugent
* 
* Library: VNLib
* Package: VNLib.Data.Caching.Global
* File: CacheNotLoadedException.cs 
*
* CacheNotLoadedException.cs is part of VNLib.Data.Caching.Global which is part of the larger 
* VNLib collection of libraries and utilities.
*
* VNLib.Data.Caching.Global is free software: you can redistribute it and/or modify 
* it under the terms of the GNU General Public License as published
* by the Free Software Foundation, either version 2 of the License,
* or (at your option) any later version.
*
* VNLib.Data.Caching.Global is distributed in the hope that it will be useful,
* but WITHOUT ANY WARRANTY; without even the implied warranty of
* MERCHANTABILITY or FITNESS FOR A PARTICULAR PURPOSE. See the GNU 
* General Public License for more details.
*
* You should have received a copy of the GNU General Public License 
* along with VNLib.Data.Caching.Global. If not, see http://www.gnu.org/licenses/.
*/

namespace VNLib.Data.Caching.Global.Exceptions
{
    public class CacheNotLoadedException : GlobalCacheException
    {
        public CacheNotLoadedException()
        { }

        public CacheNotLoadedException(string? message) : base(message)
        { }

        public CacheNotLoadedException(string? message, Exception? innerException) : base(message, innerException)
        { }
    }
}