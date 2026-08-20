namespace Armor.Core.Scheduling
{
    using System;
    using Armor.Core.Models;

    /// <summary>
    /// Pure scheduling decisions used by the agent's loop: whether a schedule is due now, and when it
    /// should next run. Keeping this logic free of timers and I/O makes it deterministic and testable.
    /// This type is stateless and thread-safe.
    /// </summary>
    public sealed class ScheduleEvaluator
    {
        /// <summary>
        /// Compute the next UTC run time for a schedule strictly after a given time.
        /// </summary>
        /// <param name="schedule">The schedule. Cannot be null.</param>
        /// <param name="afterUtc">The exclusive lower bound.</param>
        /// <returns>The next run time, or null if the cron expression has no occurrence within its search horizon.</returns>
        /// <exception cref="ArgumentNullException">Thrown when <paramref name="schedule"/> is null.</exception>
        /// <exception cref="Armor.Core.Exceptions.ArmorException">Thrown when the cron expression is invalid.</exception>
        public DateTime? ComputeNextRun(Schedule schedule, DateTime afterUtc)
        {
            if (schedule == null)
                throw new ArgumentNullException(nameof(schedule));

            CronSchedule cron = CronSchedule.Parse(schedule.CronExpression);
            return cron.NextOccurrenceUtc(afterUtc);
        }

        /// <summary>
        /// Determine whether a schedule is due at a given time. A disabled schedule is never due. When
        /// the schedule has a computed next-run time, it is due once the current time reaches it; when
        /// it does not, the cron expression is matched against the current minute.
        /// </summary>
        /// <param name="schedule">The schedule. Cannot be null.</param>
        /// <param name="nowUtc">The current time.</param>
        /// <returns>True if the schedule should run now; otherwise false.</returns>
        /// <exception cref="ArgumentNullException">Thrown when <paramref name="schedule"/> is null.</exception>
        /// <exception cref="Armor.Core.Exceptions.ArmorException">Thrown when the cron expression is invalid.</exception>
        public bool IsDue(Schedule schedule, DateTime nowUtc)
        {
            if (schedule == null)
                throw new ArgumentNullException(nameof(schedule));
            if (!schedule.Enabled)
                return false;

            if (schedule.NextRunUtc.HasValue)
                return nowUtc >= schedule.NextRunUtc.Value;

            CronSchedule cron = CronSchedule.Parse(schedule.CronExpression);
            return cron.Matches(nowUtc);
        }

        /// <summary>
        /// Record that a schedule ran at a given time, updating its last-run and next-run timestamps.
        /// </summary>
        /// <param name="schedule">The schedule to update. Cannot be null.</param>
        /// <param name="ranUtc">The time the run occurred.</param>
        /// <exception cref="ArgumentNullException">Thrown when <paramref name="schedule"/> is null.</exception>
        /// <exception cref="Armor.Core.Exceptions.ArmorException">Thrown when the cron expression is invalid.</exception>
        public void MarkRan(Schedule schedule, DateTime ranUtc)
        {
            if (schedule == null)
                throw new ArgumentNullException(nameof(schedule));

            schedule.LastRunUtc = ranUtc;
            schedule.NextRunUtc = ComputeNextRun(schedule, ranUtc);
        }
    }
}
