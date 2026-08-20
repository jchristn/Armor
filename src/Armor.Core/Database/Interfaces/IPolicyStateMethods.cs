namespace Armor.Core.Database.Interfaces
{
    using System.Collections.Generic;
    using System.Threading;
    using System.Threading.Tasks;
    using Armor.Core.Models;

    /// <summary>
    /// Data-access methods for per-policy state tables. Each policy owns a dedicated table tracking
    /// the last-seen state of every source path, which the backup engine uses for change detection.
    /// </summary>
    public interface IPolicyStateMethods
    {
        /// <summary>
        /// Ensure the per-policy state table exists. Idempotent.
        /// </summary>
        /// <param name="policyId">Policy identifier.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>A task that completes when the table exists.</returns>
        Task EnsureTableAsync(string policyId, CancellationToken token = default);

        /// <summary>
        /// Drop the per-policy state table. Idempotent.
        /// </summary>
        /// <param name="policyId">Policy identifier.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>A task that completes when the table has been dropped.</returns>
        Task DropTableAsync(string policyId, CancellationToken token = default);

        /// <summary>
        /// Read the state row for a source path.
        /// </summary>
        /// <param name="policyId">Policy identifier.</param>
        /// <param name="path">Absolute source path.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>The state row, or null if the path has no recorded state.</returns>
        Task<PolicyStateEntry?> ReadAsync(string policyId, string path, CancellationToken token = default);

        /// <summary>
        /// Read all state rows for a policy.
        /// </summary>
        /// <param name="policyId">Policy identifier.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>All state rows for the policy.</returns>
        Task<List<PolicyStateEntry>> ReadAllAsync(string policyId, CancellationToken token = default);

        /// <summary>
        /// Insert or update a state row for a source path.
        /// </summary>
        /// <param name="policyId">Policy identifier.</param>
        /// <param name="entry">The state row to persist.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>A task that completes when the row is persisted.</returns>
        Task UpsertAsync(string policyId, PolicyStateEntry entry, CancellationToken token = default);

        /// <summary>
        /// Delete the state row for a source path.
        /// </summary>
        /// <param name="policyId">Policy identifier.</param>
        /// <param name="path">Absolute source path.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>True if a row was deleted; false if none matched.</returns>
        Task<bool> DeleteAsync(string policyId, string path, CancellationToken token = default);
    }
}
