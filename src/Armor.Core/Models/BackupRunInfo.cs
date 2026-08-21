namespace Armor.Core.Models
{
    using System;
    using Armor.Core.Enums;

    /// <summary>
    /// A small, self-describing summary of one backup run, written to the storage target alongside its
    /// manifest as a metadata sidecar. It lets the catalog of available backups be listed and described
    /// without decoding the full (potentially large) manifest. It carries only run details — never the
    /// file listing, which stays inside the encrypted manifest.
    /// </summary>
    public class BackupRunInfo
    {
        /// <summary>
        /// Sidecar format version. Default is 1.
        /// </summary>
        public int FormatVersion { get; set; } = 1;

        /// <summary>
        /// Identifier of the backup run.
        /// </summary>
        public string JobId { get; set; } = String.Empty;

        /// <summary>
        /// Identifier of the policy that produced the run.
        /// </summary>
        public string PolicyId { get; set; } = String.Empty;

        /// <summary>
        /// Human-readable policy name at the time of the run, when known.
        /// </summary>
        public string? PolicyName { get; set; } = null;

        /// <summary>
        /// Backup type of the run.
        /// </summary>
        public BackupTypeEnum BackupType { get; set; } = BackupTypeEnum.Full;

        /// <summary>
        /// UTC timestamp identifying the point-in-time.
        /// </summary>
        public DateTime PointInTimeUtc { get; set; } = DateTime.UtcNow;

        /// <summary>
        /// Number of files captured in the run.
        /// </summary>
        public long FileCount { get; set; } = 0;

        /// <summary>
        /// Total source bytes captured in the run.
        /// </summary>
        public long TotalBytes { get; set; } = 0;

        /// <summary>
        /// New chunk bytes written to the target during the run.
        /// </summary>
        public long BytesWritten { get; set; } = 0;

        /// <summary>
        /// New chunks written to the target during the run.
        /// </summary>
        public long ChunksWritten { get; set; } = 0;

        /// <summary>
        /// UTC timestamp when the sidecar was written. Default is the current UTC time.
        /// </summary>
        public DateTime CreatedUtc { get; set; } = DateTime.UtcNow;

        /// <summary>
        /// Initializes a new instance of the <see cref="BackupRunInfo"/> class.
        /// </summary>
        public BackupRunInfo()
        {
        }
    }
}
