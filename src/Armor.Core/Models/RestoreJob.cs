namespace Armor.Core.Models
{
    using System;
    using Armor.Core.Enums;
    using Armor.Core.Helpers;

    /// <summary>
    /// A restore run: which backup point-in-time it read, how much of it was requested, where the
    /// output was written, and how the run finished.
    /// </summary>
    public class RestoreJob
    {
        private string _Id = IdGenerator.GenerateRestoreJobId();
        private string _BackupJobId = String.Empty;

        /// <summary>
        /// Unique, K-sortable restore identifier prefixed with <see cref="Constants.RestoreJobIdPrefix"/>.
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
        /// Identifier of the backup job (point-in-time) being restored. Cannot be null or whitespace.
        /// </summary>
        /// <exception cref="ArgumentNullException">Thrown when set to null or whitespace.</exception>
        public string BackupJobId
        {
            get
            {
                return _BackupJobId;
            }
            set
            {
                if (String.IsNullOrWhiteSpace(value))
                    throw new ArgumentNullException(nameof(BackupJobId));
                _BackupJobId = value;
            }
        }

        /// <summary>
        /// How much of the point-in-time is restored. Default is <see cref="RestoreScopeEnum.All"/>.
        /// </summary>
        public RestoreScopeEnum Scope { get; set; } = RestoreScopeEnum.All;

        /// <summary>
        /// For a folder or file scope, the source path selector within the point-in-time. Null for a
        /// full restore.
        /// </summary>
        public string? SourceSelector { get; set; } = null;

        /// <summary>
        /// Destination root the restore writes to. Null restores in place.
        /// </summary>
        public string? DestinationRoot { get; set; } = null;

        /// <summary>
        /// Current job status. Default is <see cref="JobStatusEnum.Pending"/>.
        /// </summary>
        public JobStatusEnum Status { get; set; } = JobStatusEnum.Pending;

        /// <summary>
        /// UTC timestamp when the restore started, or null if it has not started.
        /// </summary>
        public DateTime? StartedUtc { get; set; } = null;

        /// <summary>
        /// UTC timestamp when the restore finished, or null if it has not finished.
        /// </summary>
        public DateTime? CompletedUtc { get; set; } = null;

        /// <summary>
        /// Number of files restored. Default is 0.
        /// </summary>
        public long FilesRestored { get; set; } = 0;

        /// <summary>
        /// Number of bytes restored. Default is 0.
        /// </summary>
        public long BytesRestored { get; set; } = 0;

        /// <summary>
        /// Error message if the restore failed, or null on success.
        /// </summary>
        public string? Error { get; set; } = null;

        /// <summary>
        /// UTC timestamp when the restore row was created. Default is the current UTC time.
        /// </summary>
        public DateTime CreatedUtc { get; set; } = DateTime.UtcNow;

        /// <summary>
        /// Initializes a new instance of the <see cref="RestoreJob"/> class.
        /// </summary>
        public RestoreJob()
        {
        }
    }
}
