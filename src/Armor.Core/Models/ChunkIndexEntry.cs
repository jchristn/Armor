namespace Armor.Core.Models
{
    using System;
    using Armor.Core.Helpers;

    /// <summary>
    /// Tracks a single content-addressed chunk stored on a target. Chunks are identified by the
    /// SHA-256 hash of their plaintext content; the index records the chunk's presence on a specific
    /// target, its stored and plaintext sizes, and how many manifests reference it. The reference
    /// count drives deduplication on write and mark-and-sweep garbage collection on prune.
    /// </summary>
    public class ChunkIndexEntry
    {
        private string _Id = IdGenerator.GenerateChunkId();
        private string _StorageTargetId = String.Empty;
        private string _Hash = String.Empty;
        private long _StoredSizeBytes = 0;
        private long _PlaintextSizeBytes = 0;
        private long _ReferenceCount = 0;

        /// <summary>
        /// Unique, K-sortable index-entry identifier prefixed with <see cref="Constants.ChunkIdPrefix"/>.
        /// Defaults to a freshly generated identifier. Cannot be null or whitespace.
        /// </summary>
        /// <exception cref="ArgumentNullException">Thrown when set to null or whitespace.</exception>
        public string Id
        {
            get
            {
                return _Id;
            }
            set
            {
                if (String.IsNullOrWhiteSpace(value))
                    throw new ArgumentNullException(nameof(Id));
                _Id = value;
            }
        }

        /// <summary>
        /// Identifier of the storage target the chunk lives on. Deduplication is scoped per target.
        /// Cannot be null or whitespace.
        /// </summary>
        /// <exception cref="ArgumentNullException">Thrown when set to null or whitespace.</exception>
        public string StorageTargetId
        {
            get
            {
                return _StorageTargetId;
            }
            set
            {
                if (String.IsNullOrWhiteSpace(value))
                    throw new ArgumentNullException(nameof(StorageTargetId));
                _StorageTargetId = value;
            }
        }

        /// <summary>
        /// Lowercase hexadecimal SHA-256 hash of the chunk's plaintext content. This is the chunk's
        /// content address. Cannot be null or whitespace.
        /// </summary>
        /// <exception cref="ArgumentNullException">Thrown when set to null or whitespace.</exception>
        public string Hash
        {
            get
            {
                return _Hash;
            }
            set
            {
                if (String.IsNullOrWhiteSpace(value))
                    throw new ArgumentNullException(nameof(Hash));
                _Hash = value;
            }
        }

        /// <summary>
        /// Size, in bytes, of the stored chunk object on the target (after compression and
        /// encryption framing). Negative values are clamped to 0.
        /// </summary>
        public long StoredSizeBytes
        {
            get
            {
                return _StoredSizeBytes;
            }
            set
            {
                _StoredSizeBytes = value < 0 ? 0 : value;
            }
        }

        /// <summary>
        /// Size, in bytes, of the chunk's plaintext content. Negative values are clamped to 0.
        /// </summary>
        public long PlaintextSizeBytes
        {
            get
            {
                return _PlaintextSizeBytes;
            }
            set
            {
                _PlaintextSizeBytes = value < 0 ? 0 : value;
            }
        }

        /// <summary>
        /// Number of manifests currently referencing this chunk. A chunk becomes eligible for
        /// deletion when this reaches 0. Negative values are clamped to 0.
        /// </summary>
        public long ReferenceCount
        {
            get
            {
                return _ReferenceCount;
            }
            set
            {
                _ReferenceCount = value < 0 ? 0 : value;
            }
        }

        /// <summary>
        /// UTC timestamp when the chunk was first indexed. Default is the current UTC time.
        /// </summary>
        public DateTime CreatedUtc { get; set; } = DateTime.UtcNow;

        /// <summary>
        /// Initializes a new instance of the <see cref="ChunkIndexEntry"/> class.
        /// </summary>
        public ChunkIndexEntry()
        {
        }
    }
}
