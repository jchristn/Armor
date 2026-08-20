namespace Armor.Core.Models
{
    using System;
    using System.Collections.Generic;

    /// <summary>
    /// One file captured in a backup manifest: its source path, size, timestamps, attributes, and the
    /// ordered list of content-hash chunk references that reconstruct it. Unchanged files in an
    /// incremental backup still list their chunk hashes, so a restore reads a single manifest.
    /// </summary>
    public class ManifestFileEntry
    {
        private string _Path = String.Empty;
        private long _SizeBytes = 0;
        private List<string> _ChunkHashes = new List<string>();

        /// <summary>
        /// Absolute source path of the file. Cannot be null or whitespace.
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
        /// File size in bytes. Negative values are clamped to 0.
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
        /// Last-modified UTC timestamp of the file at backup time.
        /// </summary>
        public DateTime ModifiedUtc { get; set; } = DateTime.MinValue;

        /// <summary>
        /// Whether the file's archive bit was set at backup time (Windows only). Default is false.
        /// </summary>
        public bool ArchiveBit { get; set; } = false;

        /// <summary>
        /// Ordered list of lowercase hexadecimal SHA-256 chunk hashes that reconstruct the file. Never
        /// null; assigning null replaces it with an empty list. An empty list represents a zero-byte
        /// file.
        /// </summary>
        public List<string> ChunkHashes
        {
            get
            {
                return _ChunkHashes;
            }
            set
            {
                _ChunkHashes = value ?? new List<string>();
            }
        }

        /// <summary>
        /// Initializes a new instance of the <see cref="ManifestFileEntry"/> class.
        /// </summary>
        public ManifestFileEntry()
        {
        }
    }
}
