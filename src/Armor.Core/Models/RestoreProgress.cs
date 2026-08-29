namespace Armor.Core.Models
{
    /// <summary>
    /// A snapshot of a restore run's progress, reported to an optional observer as files are written.
    /// Unlike a backup, a restore knows its totals up front — they come from the backup point-in-time's
    /// manifest record — so there is no pre-scan phase: <see cref="FilesTotal"/> and
    /// <see cref="BytesTotal"/> are fixed from the first report and a caller can render a completion
    /// fraction as <see cref="BytesDone"/> over <see cref="BytesTotal"/> (or files) immediately.
    /// </summary>
    public sealed class RestoreProgress
    {
        /// <summary>
        /// Total number of files the restore expects to write (the backup point-in-time's file count).
        /// </summary>
        public int FilesTotal { get; set; } = 0;

        /// <summary>
        /// Number of files written so far.
        /// </summary>
        public int FilesDone { get; set; } = 0;

        /// <summary>
        /// Total bytes the restore expects to write (the backup point-in-time's byte total).
        /// </summary>
        public long BytesTotal { get; set; } = 0;

        /// <summary>
        /// Bytes written so far.
        /// </summary>
        public long BytesDone { get; set; } = 0;

        /// <summary>
        /// The path currently being written, or null.
        /// </summary>
        public string? CurrentPath { get; set; } = null;

        /// <summary>
        /// Initializes a new instance of the <see cref="RestoreProgress"/> class.
        /// </summary>
        public RestoreProgress()
        {
        }
    }
}
