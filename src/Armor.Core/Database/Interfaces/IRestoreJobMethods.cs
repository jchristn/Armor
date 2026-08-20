namespace Armor.Core.Database.Interfaces
{
    using System.Collections.Generic;
    using System.Threading;
    using System.Threading.Tasks;
    using Armor.Core.Models;

    /// <summary>
    /// Data-access methods for restore jobs.
    /// </summary>
    public interface IRestoreJobMethods
    {
        /// <summary>
        /// Create a restore job.
        /// </summary>
        /// <param name="job">The job to create. Its identifier is backfilled if empty.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>The created job.</returns>
        Task<RestoreJob> CreateAsync(RestoreJob job, CancellationToken token = default);

        /// <summary>
        /// Read a restore job by identifier.
        /// </summary>
        /// <param name="id">Job identifier.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>The job, or null if not found.</returns>
        Task<RestoreJob?> ReadAsync(string id, CancellationToken token = default);

        /// <summary>
        /// Read all restore jobs.
        /// </summary>
        /// <param name="token">Cancellation token.</param>
        /// <returns>All jobs, ordered by creation time.</returns>
        Task<List<RestoreJob>> ReadAllAsync(CancellationToken token = default);

        /// <summary>
        /// Update a restore job.
        /// </summary>
        /// <param name="job">The job to update.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>The updated job.</returns>
        Task<RestoreJob> UpdateAsync(RestoreJob job, CancellationToken token = default);

        /// <summary>
        /// Delete a restore job.
        /// </summary>
        /// <param name="id">Job identifier.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>True if a job was deleted; false if none matched.</returns>
        Task<bool> DeleteAsync(string id, CancellationToken token = default);

        /// <summary>
        /// Determine whether a restore job exists.
        /// </summary>
        /// <param name="id">Job identifier.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>True if the job exists; otherwise false.</returns>
        Task<bool> ExistsAsync(string id, CancellationToken token = default);
    }
}
