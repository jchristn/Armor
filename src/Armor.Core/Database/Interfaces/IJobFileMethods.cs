namespace Armor.Core.Database.Interfaces
{
    using System.Collections.Generic;
    using System.Threading;
    using System.Threading.Tasks;
    using Armor.Core.Models;

    /// <summary>
    /// Data-access methods for the <c>job_files</c> work list: the durable, per-backup-job record of every
    /// source file, its metadata, whether it has been processed, and its chunk hashes once done. The list
    /// lets a run stream its manifest to disk (instead of holding it in memory) and lets a failed run
    /// resume from where it stopped.
    /// </summary>
    public interface IJobFileMethods
    {
        /// <summary>
        /// Insert a batch of pending files for a job, each as a new row addressed by its own rowid.
        /// </summary>
        /// <param name="jobId">Backup job identifier. Cannot be null or whitespace.</param>
        /// <param name="entries">The files to add as pending. Cannot be null.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>A task that completes when the rows are persisted.</returns>
        Task AddPendingAsync(string jobId, IReadOnlyList<JobFileEntry> entries, CancellationToken token = default);

        /// <summary>
        /// Read aggregate counts for a job's work list (total files/bytes and how many are done).
        /// </summary>
        /// <param name="jobId">Backup job identifier. Cannot be null or whitespace.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>The totals; zeroes when the job has no rows.</returns>
        Task<JobFileTotals> ReadTotalsAsync(string jobId, CancellationToken token = default);

        /// <summary>
        /// Mark a file done by its rowid, recording its final metadata and ordered chunk hashes.
        /// </summary>
        /// <param name="rowid">The work-list rowid (from a page read).</param>
        /// <param name="sizeBytes">Final file size in bytes.</param>
        /// <param name="modifiedUtc">Final last-write time (UTC).</param>
        /// <param name="archiveBit">Whether the archive attribute was set.</param>
        /// <param name="chunkHashesJson">The ordered chunk hashes as a JSON array.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>A task that completes when the row is updated.</returns>
        Task MarkDoneAsync(long rowid, long sizeBytes, System.DateTime modifiedUtc, bool archiveBit, string chunkHashesJson, CancellationToken token = default);

        /// <summary>
        /// Remove a file from the work list by its rowid (for example a file that vanished between scan and
        /// copy, so it should not appear in the manifest).
        /// </summary>
        /// <param name="rowid">The work-list rowid (from a page read).</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>A task that completes when the row is removed.</returns>
        Task RemoveAsync(long rowid, CancellationToken token = default);

        /// <summary>
        /// Read the next page of pending (not-done) files, ordered by rowid. Because processed files are
        /// marked done, repeated calls advance through the remaining work.
        /// </summary>
        /// <param name="jobId">Backup job identifier. Cannot be null or whitespace.</param>
        /// <param name="limit">Maximum rows to return. Values below 1 are treated as 1.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Up to <paramref name="limit"/> pending rows; empty when none remain.</returns>
        Task<List<JobFileEntry>> ReadPendingPageAsync(string jobId, int limit, CancellationToken token = default);

        /// <summary>
        /// Read the next page of pending (not-done) files whose rowid is greater than
        /// <paramref name="afterRowid"/>, ordered by rowid. Keyset paging on the rowid lets a producer hand
        /// work to several workers concurrently without ever returning the same row twice, even while those
        /// workers are still marking earlier rows done.
        /// </summary>
        /// <param name="jobId">Backup job identifier. Cannot be null or whitespace.</param>
        /// <param name="afterRowid">Return rows whose rowid is greater than this; pass 0 to start.</param>
        /// <param name="limit">Maximum rows to return. Values below 1 are treated as 1.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Up to <paramref name="limit"/> pending rows past the cursor; empty when none remain.</returns>
        Task<List<JobFileEntry>> ReadPendingPageAsync(string jobId, long afterRowid, int limit, CancellationToken token = default);

        /// <summary>
        /// Read a page of done files ordered by rowid, starting after <paramref name="afterRowid"/>. Used
        /// to assemble the final manifest without loading every row in one query.
        /// </summary>
        /// <param name="jobId">Backup job identifier. Cannot be null or whitespace.</param>
        /// <param name="afterRowid">Return rows whose rowid is greater than this; pass 0 to start.</param>
        /// <param name="limit">Maximum rows to return. Values below 1 are treated as 1.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Up to <paramref name="limit"/> done rows; empty when none remain.</returns>
        Task<List<JobFileEntry>> ReadDonePageAsync(string jobId, long afterRowid, int limit, CancellationToken token = default);

        /// <summary>
        /// Determine whether a job still has pending files (used to decide whether a prior run is resumable).
        /// </summary>
        /// <param name="jobId">Backup job identifier. Cannot be null or whitespace.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>True when at least one not-done row exists.</returns>
        Task<bool> HasPendingAsync(string jobId, CancellationToken token = default);

        /// <summary>
        /// Delete every work-list row for a job (called after a successful finalize or a cancel).
        /// </summary>
        /// <param name="jobId">Backup job identifier. Cannot be null or whitespace.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>A task that completes when the rows are removed.</returns>
        Task DeleteByJobAsync(string jobId, CancellationToken token = default);
    }
}
