namespace VNLib.Data.Caching
{
    /// <summary>
    /// The result of a cache server change event
    /// </summary>
    public readonly struct WaitForChangeResult
    {
        /// <summary>
        /// The operation status code
        /// </summary>
        public readonly string Status { get; init; }
        /// <summary>
        /// The current (or old) id of the element that changed
        /// </summary>
        public readonly string CurrentId { get; init; }
        /// <summary>
        /// The new id of the element that changed
        /// </summary>
        public readonly string NewId { get; init; }
    }
}
