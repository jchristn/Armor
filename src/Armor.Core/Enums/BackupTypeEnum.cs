namespace Armor.Core.Enums
{
    /// <summary>
    /// Identifies how a backup run selects the files it captures relative to earlier runs.
    /// </summary>
    public enum BackupTypeEnum
    {
        /// <summary>
        /// Every included file is represented in the run's manifest. Chunks already present on the
        /// target are still skipped through deduplication, so a full run is not a full re-upload.
        /// </summary>
        Full,

        /// <summary>
        /// Only files changed since the previous run (full or incremental) are re-chunked;
        /// unchanged files re-reference existing chunks.
        /// </summary>
        Incremental,

        /// <summary>
        /// Only files changed since the last full run are re-chunked.
        /// </summary>
        Differential
    }
}
