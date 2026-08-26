namespace Armor.Core.Models
{
    using System;

    /// <summary>
    /// A file discovered by the source scan, carrying the metadata read directly from the directory
    /// enumeration (so no extra per-file stat is needed). Used to seed the per-job work list before the
    /// copy phase.
    /// </summary>
    public sealed class ScannedFile
    {
        private string _Path = String.Empty;
        private long _SizeBytes;

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
        /// Whether the file's archive attribute is set. Default is false.
        /// </summary>
        public bool ArchiveBit { get; set; } = false;

        /// <summary>
        /// Initializes a new instance of the <see cref="ScannedFile"/> class.
        /// </summary>
        public ScannedFile()
        {
        }
    }
}
