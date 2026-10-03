namespace Armor.Core.Service
{
    /// <summary>
    /// Running tallies for one scheduler tick, shared between the instrumented wrapper and the tick body
    /// so the tick's telemetry can report how many due schedules were left due. Not thread-safe; a tick
    /// runs its schedules sequentially.
    /// </summary>
    internal sealed class TickCounts
    {
        /// <summary>
        /// Schedules that were due this tick but were left due (key unavailable, target unreachable,
        /// already running, or failed).
        /// </summary>
        internal long Pending { get; set; } = 0;
    }
}
