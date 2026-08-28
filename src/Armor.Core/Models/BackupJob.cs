namespace Armor.Core.Models
{
    using System;
    using Armor.Core.Enums;
    using Armor.Core.Helpers;

    /// <summary>
    /// A single backup run and the point-in-time it produced. One row is created per run and updated
    /// as the run progresses. The <see cref="ManifestKey"/> identifies the manifest object written to
    /// the storage target for this point-in-time.
    /// </summary>
    public class BackupJob
    {
        private string _Id = IdGenerator.GenerateBackupJobId();
        private string _PolicyId = String.Empty;

        /// <summary>
        /// Unique, K-sortable job identifier prefixed with <see cref="Constants.BackupJobIdPrefix"/>.
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
        /// Identifier of the policy this run executed. Cannot be null or whitespace.
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
        /// Backup type of this run. Default is <see cref="BackupTypeEnum.Full"/>.
        /// </summary>
        public BackupTypeEnum BackupType { get; set; } = BackupTypeEnum.Full;

        /// <summary>
        /// Identifier of the baseline run for incremental or differential backups, or null for a full
        /// run or when no baseline exists.
        /// </summary>
        public string? BaseJobId { get; set; } = null;

        /// <summary>
        /// Current job status. Default is <see cref="JobStatusEnum.Pending"/>.
        /// </summary>
        public JobStatusEnum Status { get; set; } = JobStatusEnum.Pending;

        /// <summary>
        /// Storage key of the manifest object written for this point-in-time, or null until written.
        /// </summary>
        public string? ManifestKey { get; set; } = null;

        /// <summary>
        /// UTC timestamp when the run started, or null if it has not started.
        /// </summary>
        public DateTime? StartedUtc { get; set; } = null;

        /// <summary>
        /// UTC timestamp when the run finished, or null if it has not finished.
        /// </summary>
        public DateTime? CompletedUtc { get; set; } = null;

        /// <summary>
        /// Number of files captured in the manifest for this run. Default is 0.
        /// </summary>
        public long FileCount { get; set; } = 0;

        /// <summary>
        /// Number of bytes of source data represented by this run. Default is 0.
        /// </summary>
        public long BytesTotal { get; set; } = 0;

        /// <summary>
        /// Number of new chunk bytes written to the target during this run. Default is 0.
        /// </summary>
        public long BytesWritten { get; set; } = 0;

        /// <summary>
        /// Number of chunk bytes reused through deduplication during this run. Default is 0.
        /// </summary>
        public long BytesDeduplicated { get; set; } = 0;

        /// <summary>
        /// Number of new chunks written to the target during this run. Default is 0.
        /// </summary>
        public long ChunksWritten { get; set; } = 0;

        /// <summary>
        /// Number of chunks reused through deduplication during this run. Default is 0.
        /// </summary>
        public long ChunksReused { get; set; } = 0;

        /// <summary>
        /// Whether the source scan for this run finished and the work list is therefore complete. A run
        /// processes files while it is still scanning, so a run that crashes mid-scan leaves a partial work
        /// list; this flag lets a resume tell a complete list (process only) from a partial one (discard and
        /// re-scan). Default is false.
        /// </summary>
        public bool ScanComplete { get; set; } = false;

        /// <summary>
        /// Error message if the run failed, or null on success.
        /// </summary>
        public string? Error { get; set; } = null;

        /// <summary>
        /// UTC timestamp when the job row was created. Default is the current UTC time.
        /// </summary>
        public DateTime CreatedUtc { get; set; } = DateTime.UtcNow;

        /// <summary>
        /// Initializes a new instance of the <see cref="BackupJob"/> class.
        /// </summary>
        public BackupJob()
        {
        }
    }
}
