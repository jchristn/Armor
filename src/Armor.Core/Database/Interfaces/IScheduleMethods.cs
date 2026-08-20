namespace Armor.Core.Database.Interfaces
{
    using System.Collections.Generic;
    using System.Threading;
    using System.Threading.Tasks;
    using Armor.Core.Models;

    /// <summary>
    /// Data-access methods for schedules.
    /// </summary>
    public interface IScheduleMethods
    {
        /// <summary>
        /// Create a schedule.
        /// </summary>
        /// <param name="schedule">The schedule to create. Its identifier is backfilled if empty.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>The created schedule.</returns>
        Task<Schedule> CreateAsync(Schedule schedule, CancellationToken token = default);

        /// <summary>
        /// Read a schedule by identifier.
        /// </summary>
        /// <param name="id">Schedule identifier.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>The schedule, or null if not found.</returns>
        Task<Schedule?> ReadAsync(string id, CancellationToken token = default);

        /// <summary>
        /// Read all schedules.
        /// </summary>
        /// <param name="token">Cancellation token.</param>
        /// <returns>All schedules, ordered by creation time.</returns>
        Task<List<Schedule>> ReadAllAsync(CancellationToken token = default);

        /// <summary>
        /// Read all schedules for a given policy.
        /// </summary>
        /// <param name="policyId">Policy identifier.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Matching schedules, ordered by creation time.</returns>
        Task<List<Schedule>> ReadByPolicyAsync(string policyId, CancellationToken token = default);

        /// <summary>
        /// Update a schedule.
        /// </summary>
        /// <param name="schedule">The schedule to update.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>The updated schedule.</returns>
        Task<Schedule> UpdateAsync(Schedule schedule, CancellationToken token = default);

        /// <summary>
        /// Delete a schedule.
        /// </summary>
        /// <param name="id">Schedule identifier.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>True if a schedule was deleted; false if none matched.</returns>
        Task<bool> DeleteAsync(string id, CancellationToken token = default);

        /// <summary>
        /// Determine whether a schedule exists.
        /// </summary>
        /// <param name="id">Schedule identifier.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>True if the schedule exists; otherwise false.</returns>
        Task<bool> ExistsAsync(string id, CancellationToken token = default);
    }
}
