namespace Armor.Core.Models
{
    using System;
    using System.Collections.Generic;
    using Armor.Core.Enums;
    using Armor.Core.Helpers;

    /// <summary>
    /// A backup policy: the set of files and folders to include, the exclude rules and size bounds
    /// that filter them, the backup type, the retention window, and the storage target and
    /// encryption key the run uses. A policy is the unit a backup run executes against.
    /// </summary>
    public class Policy
    {
        private string _Id = IdGenerator.GeneratePolicyId();
        private string _Name = String.Empty;
        private List<string> _IncludePaths = new List<string>();
        private List<ExcludePattern> _ExcludePatterns = new List<ExcludePattern>();
        private long _MinFileSizeBytes = 0;
        private long _MaxFileSizeBytes = 0;
        private int _RetentionDays = 30;
        private int _MaxParallelism = DefaultParallelism;

        /// <summary>
        /// Smallest allowed value for <see cref="MaxParallelism"/> (fully serial).
        /// </summary>
        public const int MinParallelism = 1;

        /// <summary>
        /// Largest allowed value for <see cref="MaxParallelism"/>. Beyond this, extra workers mostly
        /// contend on the single database connection and the target disk rather than adding throughput.
        /// </summary>
        public const int MaxParallelismLimit = 32;

        /// <summary>
        /// Default number of files a run processes at once — a moderate value that speeds up the CPU-bound
        /// hashing, compression and encryption without oversaturating one disk or the shared database.
        /// </summary>
        public const int DefaultParallelism = 4;

        /// <summary>
        /// Unique, K-sortable policy identifier prefixed with <see cref="Constants.PolicyIdPrefix"/>.
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
        /// Human-readable policy name. Cannot be null or whitespace.
        /// </summary>
        /// <exception cref="ArgumentNullException">Thrown when set to null or whitespace.</exception>
        public string Name
        {
            get
            {
                return _Name;
            }
            set
            {
                if (String.IsNullOrWhiteSpace(value))
                    throw new ArgumentNullException(nameof(Name));
                _Name = value;
            }
        }

        /// <summary>
        /// Whether the policy is eligible to run. Disabled policies are skipped by the scheduler.
        /// Default is true.
        /// </summary>
        public bool Enabled { get; set; } = true;

        /// <summary>
        /// Absolute paths to the files and folders included by this policy. Never null; assigning
        /// null replaces the value with an empty list.
        /// </summary>
        public List<string> IncludePaths
        {
            get
            {
                return _IncludePaths;
            }
            set
            {
                _IncludePaths = value ?? new List<string>();
            }
        }

        /// <summary>
        /// Exclude rules applied to files and directories during enumeration. Never null; assigning
        /// null replaces the value with an empty list.
        /// </summary>
        public List<ExcludePattern> ExcludePatterns
        {
            get
            {
                return _ExcludePatterns;
            }
            set
            {
                _ExcludePatterns = value ?? new List<ExcludePattern>();
            }
        }

        /// <summary>
        /// Minimum file size, in bytes, for a file to be included. Default is 0 (no minimum).
        /// Negative values are clamped to 0.
        /// </summary>
        public long MinFileSizeBytes
        {
            get
            {
                return _MinFileSizeBytes;
            }
            set
            {
                _MinFileSizeBytes = value < 0 ? 0 : value;
            }
        }

        /// <summary>
        /// Maximum file size, in bytes, for a file to be included. Default is 0, which means no
        /// maximum. Negative values are clamped to 0.
        /// </summary>
        public long MaxFileSizeBytes
        {
            get
            {
                return _MaxFileSizeBytes;
            }
            set
            {
                _MaxFileSizeBytes = value < 0 ? 0 : value;
            }
        }

        /// <summary>
        /// Backup type used by runs of this policy. Default is <see cref="BackupTypeEnum.Full"/>.
        /// </summary>
        public BackupTypeEnum BackupType { get; set; } = BackupTypeEnum.Full;

        /// <summary>
        /// When true, and on Windows, the file archive bit is used as an additional change signal and
        /// cleared after a successful backup. Default is false, in which case change detection relies
        /// on file size and last-modified timestamp only. Has no effect on non-Windows platforms.
        /// </summary>
        public bool UseArchiveBit { get; set; } = false;

        /// <summary>
        /// Number of days a backup point-in-time is retained before it is eligible for pruning.
        /// Default is 30. Clamped to the range 1 to 3650.
        /// </summary>
        public int RetentionDays
        {
            get
            {
                return _RetentionDays;
            }
            set
            {
                _RetentionDays = Math.Clamp(value, 1, 3650);
            }
        }

        /// <summary>
        /// How many files a run of this policy processes at once. Higher values parallelize the CPU-bound
        /// hashing, compression and encryption across cores; 1 is fully serial. Default is
        /// <see cref="DefaultParallelism"/>. Clamped to the range <see cref="MinParallelism"/> to
        /// <see cref="MaxParallelismLimit"/>.
        /// </summary>
        public int MaxParallelism
        {
            get
            {
                return _MaxParallelism;
            }
            set
            {
                _MaxParallelism = Math.Clamp(value, MinParallelism, MaxParallelismLimit);
            }
        }

        /// <summary>
        /// Identifier of the storage target this policy writes to. May be null until assigned.
        /// </summary>
        public string? StorageTargetId { get; set; } = null;

        /// <summary>
        /// Identifier of the encryption key this policy uses. May be null until assigned.
        /// </summary>
        public string? EncryptionKeyId { get; set; } = null;

        /// <summary>
        /// UTC timestamp when the policy was created. Default is the current UTC time.
        /// </summary>
        public DateTime CreatedUtc { get; set; } = DateTime.UtcNow;

        /// <summary>
        /// Initializes a new instance of the <see cref="Policy"/> class.
        /// </summary>
        public Policy()
        {
        }
    }
}
