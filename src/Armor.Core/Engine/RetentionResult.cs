namespace Armor.Core.Engine
{
    /// <summary>
    /// The outcome of a retention pass: how many points-in-time were pruned and how many chunks were
    /// garbage-collected.
    /// </summary>
    public class RetentionResult
    {
        /// <summary>
        /// Number of backup points-in-time pruned.
        /// </summary>
        public int JobsPruned { get; set; } = 0;

        /// <summary>
        /// Number of chunks deleted because no surviving manifest referenced them.
        /// </summary>
        public int ChunksDeleted { get; set; } = 0;

        /// <summary>
        /// Initializes a new instance of the <see cref="RetentionResult"/> class.
        /// </summary>
        public RetentionResult()
        {
        }
    }
}
