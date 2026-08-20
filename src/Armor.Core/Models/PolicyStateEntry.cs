namespace Armor.Core.Models
{
    using System;

    /// <summary>
    /// One source-path row in a policy's per-policy state table. It records what Armor last saw for a
    /// path so change detection can decide, cheaply, whether the file needs re-chunking on the next
    /// run. Each policy owns its own state table.
    /// </summary>
    public class PolicyStateEntry
    {
        private string _Path = String.Empty;
        private long _SizeBytes = 0;

        /// <summary>
        /// Absolute source path this row describes. Serves as the row key within the policy's state
        /// table. Cannot be null or whitespace.
        /// </summary>
        /// <exception cref="ArgumentNullException">Thrown when set to null or whitespace.</exception>
        public string Path
        {
            get
            {
                return _Path;
            }
            set
            {
                if (String.IsNullOrWhiteSpace(value))
                    throw new ArgumentNullException(nameof(Path));
                _Path = value;
            }
        }

        /// <summary>
        /// Size, in bytes, of the file when last backed up. Negative values are clamped to 0.
        /// </summary>
        public long SizeBytes
        {
            get
            {
                return _SizeBytes;
            }
            set
            {
                _SizeBytes = value < 0 ? 0 : value;
            }
        }

        /// <summary>
        /// Last-modified UTC timestamp of the file when last backed up.
        /// </summary>
        public DateTime ModifiedUtc { get; set; } = DateTime.MinValue;

        /// <summary>
        /// Whether the file's archive bit was set when last observed (Windows only). Default is false.
        /// </summary>
        public bool ArchiveBit { get; set; } = false;

        /// <summary>
        /// Lowercase hexadecimal hash of the ordered chunk list produced for this file on the last
        /// successful backup. A change here indicates the file's content changed. Null until the file
        /// has been backed up at least once.
        /// </summary>
        public string? ChunkListHash { get; set; } = null;

        /// <summary>
        /// Identifier of the last backup job that captured this path, or null if none.
        /// </summary>
        public string? LastJobId { get; set; } = null;

        /// <summary>
        /// UTC timestamp when this state row was last updated. Default is the current UTC time.
        /// </summary>
        public DateTime UpdatedUtc { get; set; } = DateTime.UtcNow;

        /// <summary>
        /// Initializes a new instance of the <see cref="PolicyStateEntry"/> class.
        /// </summary>
        public PolicyStateEntry()
        {
        }
    }
}
