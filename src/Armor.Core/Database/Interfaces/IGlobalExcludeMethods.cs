namespace Armor.Core.Database.Interfaces
{
    using System.Collections.Generic;
    using System.Threading;
    using System.Threading.Tasks;
    using Armor.Core.Models;

    /// <summary>
    /// Data-access methods for the shared global exclude list — a single ordered set of exclude rules
    /// applied to every policy that opts in via <see cref="Policy.UseGlobalExcludes"/>.
    /// </summary>
    public interface IGlobalExcludeMethods
    {
        /// <summary>
        /// Read the global exclude patterns in their stored order.
        /// </summary>
        /// <param name="token">Cancellation token.</param>
        /// <returns>The global exclude patterns.</returns>
        Task<List<ExcludePattern>> ReadAllAsync(CancellationToken token = default);

        /// <summary>
        /// Replace the entire global exclude list with the supplied patterns.
        /// </summary>
        /// <param name="patterns">The new list. Null is treated as empty.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>A task that completes when the list has been replaced.</returns>
        Task ReplaceAllAsync(IEnumerable<ExcludePattern> patterns, CancellationToken token = default);

        /// <summary>
        /// Replace the global exclude list with the built-in defaults (see <see cref="GlobalExcludeDefaults"/>).
        /// </summary>
        /// <param name="token">Cancellation token.</param>
        /// <returns>The default patterns that were stored.</returns>
        Task<List<ExcludePattern>> ResetToDefaultsAsync(CancellationToken token = default);
    }
}
