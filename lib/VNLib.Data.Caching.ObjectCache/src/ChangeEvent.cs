/*
* Copyright (c) 2022 Vaughn Nugent
* 
* Library: VNLib
* Package: VNLib.Data.Caching.ObjectCache
* File: ChangeEvent.cs 
*
* ChangeEvent.cs is part of VNLib.Data.Caching.ObjectCache which is part of the larger 
* VNLib collection of libraries and utilities.
*
* VNLib.Data.Caching.ObjectCache is free software: you can redistribute it and/or modify 
* it under the terms of the GNU Affero General Public License as 
* published by the Free Software Foundation, either version 3 of the
* License, or (at your option) any later version.
*
* VNLib.Data.Caching.ObjectCache is distributed in the hope that it will be useful,
* but WITHOUT ANY WARRANTY; without even the implied warranty of
* MERCHANTABILITY or FITNESS FOR A PARTICULAR PURPOSE. See the
* GNU Affero General Public License for more details.
*
* You should have received a copy of the GNU Affero General Public License
* along with this program.  If not, see https://www.gnu.org/licenses/.
*/

namespace VNLib.Data.Caching.ObjectCache
{
    /// <summary>
    /// An event object that is passed when change events occur
    /// </summary>
    public class ChangeEvent
    {
        public readonly string CurrentId;
        public readonly string? AlternateId;
        public readonly bool Deleted;
        internal ChangeEvent(string id, string? alternate, bool deleted)
        {
            CurrentId = id;
            AlternateId = alternate;
            Deleted = deleted;
        }
    }
}
