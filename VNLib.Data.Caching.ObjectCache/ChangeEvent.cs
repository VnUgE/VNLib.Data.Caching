
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
