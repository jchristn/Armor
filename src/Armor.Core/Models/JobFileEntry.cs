namespace Armor.Core.Models
{
    using System;
    using System.Collections.Generic;

    /// <summary>
    /// One row of a backup job's durable work list (<c>job_files</c>): a source file with its metadata,
    /// a done flag, and — once processed — the ordered chunk hashes that make up the file. The work list
    /// lets a run stream its manifest to disk instead of holding it in memory, and lets a failed run pick
    /// up where it left off.
    /// </summary>
    public sealed class JobFileEntry
    {
        private string _Path = String.Empty;
        private long _SizeBytes;
        private List<string> _ChunkHashes = new List<string>();

        /// <summary>
        /// Row identifier (rowid) within the work-list table, used to update or remove the row directly.
        /// Zero for a row that has not been read back from the database. Default is 0.
        /// </summary>
        public long Rowid { get; set; }

        /// <summary>
        /// Absolute path of the file. Cannot be null or whitespace.
        /// </summary>
        /// <exception cref="ArgumentNullException">Thrown when set to null or whitespace.</exception>
        public string Path
        {
            get { return _Path; }
            set
            {
                if (String.IsNullOrWhiteSpace(value))
                    throw new ArgumentNullException(nameof(Path));
                _Path = value;
            }
        }

        /// <summary>
        /// File size in bytes. Negative values are clamped to zero. Default is 0.
        /// </summary>
        public long SizeBytes
        {
            get { return _SizeBytes; }
            set { _SizeBytes = value < 0 ? 0 : value; }
        }

        /// <summary>
        /// Last-write time (UTC). Default is <see cref="DateTime.MinValue"/>.
        /// </summary>
        public DateTime ModifiedUtc { get; set; } = DateTime.MinValue;

        /// <summary>
        /// Whether the file's archive attribute was set. Default is false.
        /// </summary>
        public bool ArchiveBit { get; set; } = false;

        /// <summary>
        /// True once the file has been chunked (or reused from the baseline) and its chunks are on the
        /// target. Default is false.
        /// </summary>
        public bool Done { get; set; } = false;

        /// <summary>
        /// Ordered chunk hashes composing the file, populated once <see cref="Done"/> is true. Never null;
        /// assigning null yields an empty list.
        /// </summary>
        public List<string> ChunkHashes
        {
            get { return _ChunkHashes; }
            set { _ChunkHashes = value ?? new List<string>(); }
        }

        /// <summary>
        /// Initializes a new instance of the <see cref="JobFileEntry"/> class.
        /// </summary>
        public JobFileEntry()
        {
        }
    }
}
