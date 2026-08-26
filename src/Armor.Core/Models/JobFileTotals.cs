namespace Armor.Core.Models
{
    /// <summary>
    /// Aggregate counts over a backup job's work list: how many files and bytes it covers in total, and
    /// how many are already done. Used to drive the progress bar (including a run that resumes partway).
    /// </summary>
    public sealed class JobFileTotals
    {
        /// <summary>
        /// Total number of files in the work list.
        /// </summary>
        public int FileCount { get; set; }

        /// <summary>
        /// Total source bytes across the work list.
        /// </summary>
        public long TotalBytes { get; set; }

        /// <summary>
        /// Number of files already marked done.
        /// </summary>
        public int DoneCount { get; set; }

        /// <summary>
        /// Source bytes of the files already marked done.
        /// </summary>
        public long DoneBytes { get; set; }

        /// <summary>
        /// Initializes a new instance of the <see cref="JobFileTotals"/> class.
        /// </summary>
        public JobFileTotals()
        {
        }
    }
}
