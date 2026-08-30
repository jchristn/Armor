namespace Armor.Core.Database.Interfaces
{
    using System.Collections.Generic;
    using System.Threading;
    using System.Threading.Tasks;
    using Armor.Core.Models;

    /// <summary>
    /// Data-access methods for backup jobs (runs / points-in-time).
    /// </summary>
    public interface IBackupJobMethods
    {
        /// <summary>
        /// Create a backup job.
        /// </summary>
        /// <param name="job">The job to create. Its identifier is backfilled if empty.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>The created job.</returns>
        Task<BackupJob> CreateAsync(BackupJob job, CancellationToken token = default);

        /// <summary>
        /// Read a backup job by identifier.
        /// </summary>
        /// <param name="id">Job identifier.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>The job, or null if not found.</returns>
        Task<BackupJob?> ReadAsync(string id, CancellationToken token = default);

        /// <summary>
        /// Read all backup jobs.
        /// </summary>
        /// <param name="token">Cancellation token.</param>
        /// <returns>All jobs, ordered by creation time.</returns>
        Task<List<BackupJob>> ReadAllAsync(CancellationToken token = default);

        /// <summary>
        /// Read all backup jobs for a policy, most recent first.
        /// </summary>
        /// <param name="policyId">Policy identifier.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Matching jobs, ordered newest first.</returns>
        Task<List<BackupJob>> ReadByPolicyAsync(string policyId, CancellationToken token = default);

        /// <summary>
        /// Read the most recent completed backup job for a policy.
        /// </summary>
        /// <param name="policyId">Policy identifier.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>The latest completed job, or null if none exists.</returns>
        Task<BackupJob?> ReadLatestCompletedAsync(string policyId, CancellationToken token = default);

        /// <summary>
        /// Read the most recent completed full backup job for a policy.
        /// </summary>
        /// <param name="policyId">Policy identifier.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>The latest completed full job, or null if none exists.</returns>
        Task<BackupJob?> ReadLatestCompletedFullAsync(string policyId, CancellationToken token = default);

        /// <summary>
        /// Update a backup job.
        /// </summary>
        /// <param name="job">The job to update.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>The updated job.</returns>
        Task<BackupJob> UpdateAsync(BackupJob job, CancellationToken token = default);

        /// <summary>
        /// Delete a backup job.
        /// </summary>
        /// <param name="id">Job identifier.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>True if a job was deleted; false if none matched.</returns>
        Task<bool> DeleteAsync(string id, CancellationToken token = default);

        /// <summary>
        /// Set only the scan-complete flag on a job, without touching its other fields. Used by a live run to
        /// durably record that its source scan finished, so a resume after a later crash processes the
        /// complete work list instead of re-scanning.
        /// </summary>
        /// <param name="jobId">Backup job identifier. Cannot be null or whitespace.</param>
        /// <param name="complete">The flag value to store.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>A task that completes when the flag is written.</returns>
        Task SetScanCompleteAsync(string jobId, bool complete, CancellationToken token = default);

        /// <summary>
        /// Update only the live-progress columns of a running job — file count, total bytes, and bytes
        /// written — without touching its other fields. Called periodically during a run so another process
        /// (for example the TUI's in-progress view) can watch the run advance; the authoritative final totals
        /// are written when the run finishes.
        /// </summary>
        /// <param name="jobId">Backup job identifier. Cannot be null or whitespace.</param>
        /// <param name="fileCount">Files processed so far.</param>
        /// <param name="bytesTotal">Total source bytes known so far (grows while scanning).</param>
        /// <param name="bytesWritten">Bytes written to the target so far.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>A task that completes when the progress is written.</returns>
        Task UpdateProgressAsync(string jobId, long fileCount, long bytesTotal, long bytesWritten, CancellationToken token = default);

        /// <summary>
        /// Determine whether a backup job exists.
        /// </summary>
        /// <param name="id">Job identifier.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>True if the job exists; otherwise false.</returns>
        Task<bool> ExistsAsync(string id, CancellationToken token = default);
    }
}
