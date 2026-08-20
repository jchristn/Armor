namespace Armor.Core.Database.Interfaces
{
    using System.Collections.Generic;
    using System.Threading;
    using System.Threading.Tasks;
    using Armor.Core.Models;

    /// <summary>
    /// Data-access methods for backup policies, including their include paths and exclude patterns.
    /// </summary>
    public interface IPolicyMethods
    {
        /// <summary>
        /// Create a policy and its child collections.
        /// </summary>
        /// <param name="policy">The policy to create. Its identifier is backfilled if empty.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>The created policy.</returns>
        Task<Policy> CreateAsync(Policy policy, CancellationToken token = default);

        /// <summary>
        /// Read a policy by identifier.
        /// </summary>
        /// <param name="id">Policy identifier.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>The policy, or null if not found.</returns>
        Task<Policy?> ReadAsync(string id, CancellationToken token = default);

        /// <summary>
        /// Read all policies.
        /// </summary>
        /// <param name="token">Cancellation token.</param>
        /// <returns>All policies, ordered by creation time.</returns>
        Task<List<Policy>> ReadAllAsync(CancellationToken token = default);

        /// <summary>
        /// Update a policy and replace its child collections.
        /// </summary>
        /// <param name="policy">The policy to update.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>The updated policy.</returns>
        Task<Policy> UpdateAsync(Policy policy, CancellationToken token = default);

        /// <summary>
        /// Delete a policy and its child collections.
        /// </summary>
        /// <param name="id">Policy identifier.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>True if a policy was deleted; false if none matched.</returns>
        Task<bool> DeleteAsync(string id, CancellationToken token = default);

        /// <summary>
        /// Determine whether a policy exists.
        /// </summary>
        /// <param name="id">Policy identifier.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>True if the policy exists; otherwise false.</returns>
        Task<bool> ExistsAsync(string id, CancellationToken token = default);
    }
}
