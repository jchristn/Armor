namespace Armor.Core.Models
{
    /// <summary>
    /// A snapshot of a backup run's progress, reported to an optional observer as files are processed.
    /// Totals are established by a lightweight pre-scan; a caller can render a completion fraction as
    /// <see cref="BytesDone"/> over <see cref="BytesTotal"/> (or files) and show the current path.
    /// </summary>
    public sealed class BackupProgress
    {
        /// <summary>
        /// Total number of files the run expects to process (from the pre-scan).
        /// </summary>
        public int FilesTotal { get; set; } = 0;

        /// <summary>
        /// Number of files processed so far.
        /// </summary>
        public int FilesDone { get; set; } = 0;

        /// <summary>
        /// Total source bytes the run expects to process (from the pre-scan).
        /// </summary>
        public long BytesTotal { get; set; } = 0;

        /// <summary>
        /// Source bytes processed so far.
        /// </summary>
        public long BytesDone { get; set; } = 0;

        /// <summary>
        /// The path currently being processed, or null.
        /// </summary>
        public string? CurrentPath { get; set; } = null;

        /// <summary>
        /// True while the run is still pre-scanning the source to establish totals (before any files are
        /// copied). During this phase <see cref="FilesTotal"/> and <see cref="BytesTotal"/> climb while
        /// <see cref="FilesDone"/> stays zero, so an observer can show a "scanning" state rather than an
        /// empty progress bar. False once copying begins. Default is false.
        /// </summary>
        public bool Scanning { get; set; } = false;

        /// <summary>
        /// Initializes a new instance of the <see cref="BackupProgress"/> class.
        /// </summary>
        public BackupProgress()
        {
        }
    }
}
