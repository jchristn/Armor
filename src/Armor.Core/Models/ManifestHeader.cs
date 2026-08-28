namespace Armor.Core.Models
{
    using System;
    using Armor.Core.Enums;

    /// <summary>
    /// The small, fixed-size head of a segmented (format 2) manifest. It carries the run's identity and
    /// point-in-time plus the counts needed to read the manifest — how many segment objects follow and how
    /// many files and bytes they describe — without loading any file entries. The file entries themselves
    /// live in the numbered segment objects beside the header, each independently compressed and encrypted,
    /// so neither writing nor reading a manifest ever holds the whole file list in memory.
    /// </summary>
    public class ManifestHeader
    {
        private string _JobId = String.Empty;
        private string _PolicyId = String.Empty;

        /// <summary>
        /// Manifest format version. 2 identifies a segmented manifest. Default is 2.
        /// </summary>
        public int FormatVersion { get; set; } = 2;

        /// <summary>
        /// Identifier of the backup job that produced this manifest. Cannot be null or whitespace.
        /// </summary>
        /// <exception cref="ArgumentNullException">Thrown when set to null or whitespace.</exception>
        public string JobId
        {
            get { return _JobId; }
            set
            {
                if (String.IsNullOrWhiteSpace(value))
                    throw new ArgumentNullException(nameof(JobId));
                _JobId = value;
            }
        }

        /// <summary>
        /// Identifier of the policy this manifest belongs to. Cannot be null or whitespace.
        /// </summary>
        /// <exception cref="ArgumentNullException">Thrown when set to null or whitespace.</exception>
        public string PolicyId
        {
            get { return _PolicyId; }
            set
            {
                if (String.IsNullOrWhiteSpace(value))
                    throw new ArgumentNullException(nameof(PolicyId));
                _PolicyId = value;
            }
        }

        /// <summary>
        /// Backup type of the run. Default is <see cref="BackupTypeEnum.Full"/>.
        /// </summary>
        public BackupTypeEnum BackupType { get; set; } = BackupTypeEnum.Full;

        /// <summary>
        /// Identifier of the baseline run for incremental or differential backups, or null.
        /// </summary>
        public string? BaseJobId { get; set; } = null;

        /// <summary>
        /// UTC timestamp identifying the point-in-time.
        /// </summary>
        public DateTime PointInTimeUtc { get; set; } = DateTime.UtcNow;

        /// <summary>
        /// Number of segment objects that follow the header. Negative values are clamped to 0.
        /// </summary>
        public int SegmentCount
        {
            get { return _SegmentCount; }
            set { _SegmentCount = value < 0 ? 0 : value; }
        }
        private int _SegmentCount;

        /// <summary>
        /// Total number of files described across all segments. Negative values are clamped to 0.
        /// </summary>
        public long FileCount
        {
            get { return _FileCount; }
            set { _FileCount = value < 0 ? 0 : value; }
        }
        private long _FileCount;

        /// <summary>
        /// Total plaintext bytes of all files described across all segments. Negative values are clamped to 0.
        /// </summary>
        public long TotalBytes
        {
            get { return _TotalBytes; }
            set { _TotalBytes = value < 0 ? 0 : value; }
        }
        private long _TotalBytes;

        /// <summary>
        /// Initializes a new instance of the <see cref="ManifestHeader"/> class.
        /// </summary>
        public ManifestHeader()
        {
        }
    }
}
