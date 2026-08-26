namespace Armor.Core.Service
{
    using System;
    using System.Collections.Generic;
    using System.Threading;
    using System.Threading.Tasks;
    using Armor.Core.Models;
    using Armor.Core.Scheduling;

    /// <summary>
    /// Evaluates schedules and runs the ones that are due. One tick is a pure, injectable operation:
    /// the current time and a key provider are passed in, so the agent's timer loop stays a thin
    /// wrapper and the decision logic is testable. A schedule whose policy's data key is unavailable is
    /// left due so it runs as soon as the key is unlocked, rather than being skipped forward.
    /// </summary>
    public sealed class SchedulerService
    {
        private readonly ArmorContext _Context;
        private readonly ScheduleEvaluator _Evaluator = new ScheduleEvaluator();

        /// <summary>
        /// Initializes a new instance of the <see cref="SchedulerService"/> class.
        /// </summary>
        /// <param name="context">The runtime context. Cannot be null.</param>
        /// <exception cref="ArgumentNullException">Thrown when <paramref name="context"/> is null.</exception>
        public SchedulerService(ArmorContext context)
        {
            _Context = context ?? throw new ArgumentNullException(nameof(context));
        }

        /// <summary>
        /// Run one scheduling tick: run every enabled, due schedule whose policy key is available.
        /// </summary>
        /// <param name="keyProvider">
        /// Returns the unlocked data key for a policy, or null when the key is not available. Cannot be
        /// null.
        /// </param>
        /// <param name="nowUtc">The current time.</param>
        /// <param name="token">Cancellation token.</param>
        /// <param name="onError">Optional callback invoked when a single schedule's backup fails; the tick continues with the rest.</param>
        /// <returns>The number of schedules that ran a backup.</returns>
        /// <exception cref="ArgumentNullException">Thrown when <paramref name="keyProvider"/> is null.</exception>
        public async Task<int> TickAsync(Func<Policy, Task<byte[]?>> keyProvider, DateTime nowUtc, CancellationToken token = default, Action<Schedule, Exception>? onError = null)
        {
            if (keyProvider == null)
                throw new ArgumentNullException(nameof(keyProvider));

            int ran = 0;
            List<Schedule> schedules = await _Context.Database.Schedules.ReadAllAsync(token).ConfigureAwait(false);
            BackupService backupService = new BackupService(_Context);

            foreach (Schedule schedule in schedules)
            {
                token.ThrowIfCancellationRequested();
                if (!schedule.Enabled)
                    continue;

                if (!schedule.NextRunUtc.HasValue)
                {
                    schedule.NextRunUtc = _Evaluator.ComputeNextRun(schedule, nowUtc);
                    await _Context.Database.Schedules.UpdateAsync(schedule, token).ConfigureAwait(false);
                    continue;
                }

                if (!_Evaluator.IsDue(schedule, nowUtc))
                    continue;

                Policy? policy = await _Context.Database.Policies.ReadAsync(schedule.PolicyId, token).ConfigureAwait(false);
                if (policy == null || !policy.Enabled)
                {
                    _Evaluator.MarkRan(schedule, nowUtc);
                    await _Context.Database.Schedules.UpdateAsync(schedule, token).ConfigureAwait(false);
                    continue;
                }

                byte[]? dataKey = await keyProvider(policy).ConfigureAwait(false);
                if (dataKey == null)
                    continue;

                try
                {
                    await backupService.RunAsync(policy.Id, dataKey, null, true, token).ConfigureAwait(false);
                }
                catch (OperationCanceledException)
                {
                    throw;
                }
                catch (Exception ex)
                {
                    // One policy's failure (for example an unreachable target) must not abort the tick
                    // or starve the other schedules. Leave this one due so it retries next tick — and
                    // runs the moment the target is reachable again.
                    onError?.Invoke(schedule, ex);
                    continue;
                }

                _Evaluator.MarkRan(schedule, nowUtc);
                await _Context.Database.Schedules.UpdateAsync(schedule, token).ConfigureAwait(false);
                ran += 1;
            }

            return ran;
        }
    }
}
