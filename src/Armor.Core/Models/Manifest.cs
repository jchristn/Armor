namespace Armor.Core.Models
{
    using System;
    using System.Collections.Generic;
    using Armor.Core.Enums;

    /// <summary>
    /// The record of a single backup point-in-time: which policy and run produced it, its backup type
    /// and baseline, and the full list of files with their chunk references. A manifest is serialized,
    /// compressed, and encrypted, then stored on the target; a restore reads exactly one manifest to
    /// reconstruct the point-in-time.
    /// </summary>
    public class Manifest
    {
        private int _FormatVersion = 1;
        private string _JobId = String.Empty;
        private string _PolicyId = String.Empty;
        private List<ManifestFileEntry> _Files = new List<ManifestFileEntry>();

        /// <summary>
        /// Manifest format version. Default is 1. Clamped to a minimum of 1.
        /// </summary>
        public int FormatVersion
        {
            get
            {
                return _FormatVersion;
            }
            set
            {
                _FormatVersion = value < 1 ? 1 : value;
            }
        }

        /// <summary>
        /// Identifier of the backup job that produced this manifest. Cannot be null or whitespace.
        /// </summary>
        /// <exception cref="ArgumentNullException">Thrown when set to null or whitespace.</exception>
        public string JobId
        {
            get
            {
                return _JobId;
            }
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
            get
            {
                return _PolicyId;
            }
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
        /// Files captured in this point-in-time. Never null; assigning null replaces it with an empty
        /// list.
        /// </summary>
        public List<ManifestFileEntry> Files
        {
            get
            {
                return _Files;
            }
            set
            {
                _Files = value ?? new List<ManifestFileEntry>();
            }
        }

        /// <summary>
        /// Initializes a new instance of the <see cref="Manifest"/> class.
        /// </summary>
        public Manifest()
        {
        }
    }
}
