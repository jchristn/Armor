namespace Armor.Core.Database.Interfaces
{
    using System.Collections.Generic;
    using System.Threading;
    using System.Threading.Tasks;
    using Armor.Core.Models;

    /// <summary>
    /// Data-access methods for storage targets.
    /// </summary>
    public interface IStorageTargetMethods
    {
        /// <summary>
        /// Create a storage target.
        /// </summary>
        /// <param name="target">The target to create. Its identifier is backfilled if empty.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>The created target.</returns>
        Task<StorageTarget> CreateAsync(StorageTarget target, CancellationToken token = default);

        /// <summary>
        /// Read a storage target by identifier.
        /// </summary>
        /// <param name="id">Target identifier.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>The target, or null if not found.</returns>
        Task<StorageTarget?> ReadAsync(string id, CancellationToken token = default);

        /// <summary>
        /// Read all storage targets.
        /// </summary>
        /// <param name="token">Cancellation token.</param>
        /// <returns>All targets, ordered by creation time.</returns>
        Task<List<StorageTarget>> ReadAllAsync(CancellationToken token = default);

        /// <summary>
        /// Update a storage target.
        /// </summary>
        /// <param name="target">The target to update.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>The updated target.</returns>
        Task<StorageTarget> UpdateAsync(StorageTarget target, CancellationToken token = default);

        /// <summary>
        /// Delete a storage target.
        /// </summary>
        /// <param name="id">Target identifier.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>True if a target was deleted; false if none matched.</returns>
        Task<bool> DeleteAsync(string id, CancellationToken token = default);

        /// <summary>
        /// Determine whether a storage target exists.
        /// </summary>
        /// <param name="id">Target identifier.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>True if the target exists; otherwise false.</returns>
        Task<bool> ExistsAsync(string id, CancellationToken token = default);
    }
}
