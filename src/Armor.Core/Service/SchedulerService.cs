namespace Armor.Core.Service
{
    using System;
    using System.Collections.Generic;
    using System.Diagnostics;
    using System.Threading;
    using System.Threading.Tasks;
    using Armor.Core.Exceptions;
    using Armor.Core.Models;
    using Armor.Core.Scheduling;
    using Armor.Core.Telemetry;

    /// <summary>
    /// Evaluates schedules and runs the ones that are due. One tick is a pure, injectable operation:
    /// the current time and a key provider are passed in, so the agent's timer loop stays a thin
    /// wrapper and the decision logic is testable. A schedule whose policy's data key is unavailable is
    /// left due so it runs as soon as the key is unlocked, rather than being skipped forward. A schedule
    /// whose backup fails is also left due, but retried with exponential backoff (doubling from
    /// <see cref="InitialBackoff"/> up to <see cref="MaxBackoff"/>) so a persistent failure is not retried,
    /// recorded, and reported on every tick. Backoff state is held in memory and resets when the instance
    /// is recreated; reuse one instance across ticks for it to take effect.
    /// </summary>
    public sealed class SchedulerService
    {
        /// <summary>
        /// Delay before the first retry of a failed schedule.
        /// </summary>
        public static readonly TimeSpan InitialBackoff = TimeSpan.FromMinutes(1);

        /// <summary>
        /// Upper bound on the delay between retries of a failing schedule.
        /// </summary>
        public static readonly TimeSpan MaxBackoff = TimeSpan.FromHours(1);

        private readonly ArmorContext _Context;
        private readonly ScheduleEvaluator _Evaluator = new ScheduleEvaluator();
        private readonly Dictionary<string, FailureState> _Failures = new Dictionary<string, FailureState>(StringComparer.Ordinal);

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
        /// <param name="onCompleted">Optional callback invoked after a schedule's backup completes successfully, with the policy and the finished job so the host can report its result.</param>
        /// <returns>The number of schedules that ran a backup.</returns>
        /// <exception cref="ArgumentNullException">Thrown when <paramref name="keyProvider"/> is null.</exception>
        public async Task<int> TickAsync(Func<Policy, Task<byte[]?>> keyProvider, DateTime nowUtc, CancellationToken token = default, Action<Schedule, Exception>? onError = null, Action<Schedule, Policy, BackupJob>? onCompleted = null)
        {
            if (keyProvider == null)
                throw new ArgumentNullException(nameof(keyProvider));

            // Root span of one tick; each backup the tick runs nests under it as backup.run.
            long start = Stopwatch.GetTimestamp();
            Activity? activity = ArmorTelemetry.StartActivity(TelemetryNames.SpanSchedulerTick);
            TickCounts counts = new TickCounts();
            string outcome = TelemetryNames.OutcomeFailure;
            string? errorType = null;
            try
            {
                int ran = await TickCoreAsync(keyProvider, nowUtc, token, onError, onCompleted, counts).ConfigureAwait(false);
                outcome = TelemetryNames.OutcomeSuccess;
                ArmorTelemetry.SetTag(activity, TelemetryNames.AttrSchedulesRan, ran);
                ArmorTelemetry.SetTag(activity, "armor.schedules.pending", counts.Pending);
                ArmorTelemetry.MarkSuccess(activity);
                return ran;
            }
            catch (Exception ex)
            {
                outcome = ArmorTelemetry.OutcomeOf(ex);
                if (outcome == TelemetryNames.OutcomeFailure)
                {
                    errorType = ArmorTelemetry.ErrorType(ex);
                    ArmorTelemetry.RecordError(TelemetryNames.ComponentScheduler, ex);
                }
                ArmorTelemetry.MarkException(activity, ex);
                throw;
            }
            finally
            {
                if (outcome != TelemetryNames.OutcomeCanceled)
                    ArmorTelemetry.NoteSchedulerTick(DateTime.UtcNow, counts.Pending);
                TagList tags = new TagList();
                tags.Add(TelemetryNames.AttrOutcome, outcome);
                ArmorTelemetry.Record(ArmorTelemetry.SchedulerTickDuration, Stopwatch.GetElapsedTime(start).TotalSeconds, tags);
                if (errorType != null)
                    tags.Add(TelemetryNames.AttrErrorType, errorType);
                ArmorTelemetry.Add(ArmorTelemetry.SchedulerTicks, 1, tags);
                ArmorTelemetry.Stop(activity);
            }
        }

        private async Task<int> TickCoreAsync(Func<Policy, Task<byte[]?>> keyProvider, DateTime nowUtc, CancellationToken token, Action<Schedule, Exception>? onError, Action<Schedule, Policy, BackupJob>? onCompleted, TickCounts counts)
        {
            int ran = 0;
            List<Schedule> schedules = await _Context.Database.Schedules.ReadAllAsync(token).ConfigureAwait(false);
            BackupService backupService = new BackupService(_Context);

            foreach (Schedule schedule in schedules)
            {
                token.ThrowIfCancellationRequested();
                if (!schedule.Enabled)
                {
                    RecordDecision(TelemetryNames.DecisionDisabled);
                    continue;
                }

                if (!schedule.NextRunUtc.HasValue)
                {
                    schedule.NextRunUtc = _Evaluator.ComputeNextRun(schedule, nowUtc);
                    await _Context.Database.Schedules.UpdateAsync(schedule, token).ConfigureAwait(false);
                    RecordDecision(TelemetryNames.DecisionInitialized);
                    continue;
                }

                if (!_Evaluator.IsDue(schedule, nowUtc))
                {
                    RecordDecision(TelemetryNames.DecisionNotDue);
                    continue;
                }

                FailureState? failure;
                if (_Failures.TryGetValue(schedule.Id, out failure) && nowUtc < failure.RetryAtUtc)
                {
                    RecordDecision(TelemetryNames.DecisionBackoff);
                    counts.Pending++;
                    continue;
                }

                Policy? policy = await _Context.Database.Policies.ReadAsync(schedule.PolicyId, token).ConfigureAwait(false);
                if (policy == null || !policy.Enabled)
                {
                    _Evaluator.MarkRan(schedule, nowUtc);
                    await _Context.Database.Schedules.UpdateAsync(schedule, token).ConfigureAwait(false);
                    RecordDecision(TelemetryNames.DecisionPolicyDisabled);
                    continue;
                }

                byte[]? dataKey = await keyProvider(policy).ConfigureAwait(false);
                if (dataKey == null)
                {
                    RecordDecision(TelemetryNames.DecisionKeyUnavailable);
                    counts.Pending++;
                    continue;
                }

                BackupJob job;
                try
                {
                    job = await backupService.RunAsync(policy.Id, dataKey, null, true, token).ConfigureAwait(false);
                }
                catch (OperationCanceledException)
                {
                    throw;
                }
                catch (TargetUnreachableException)
                {
                    // The target is temporarily unavailable — most often a removable/USB drive that is not
                    // connected right now. This is not a failure: leave the schedule due (do not MarkRan) and
                    // do not report an error, so no failure is recorded and no notification fires every tick.
                    // The backup runs on the next tick after the drive is reconnected. Other schedules whose
                    // targets are reachable — an S3 policy, say — are unaffected.
                    Diagnostics.ArmorLog.Debug("Skipping due schedule for policy '" + policy.Name + "': its target is not reachable yet. Will retry.");
                    RecordDecision(TelemetryNames.DecisionTargetUnreachable);
                    counts.Pending++;
                    continue;
                }
                catch (PolicyAlreadyRunningException)
                {
                    // The policy is already backing up (a manual run holds the lock). Leave the schedule due
                    // and retry next tick rather than recording a failure for a run that is proceeding fine.
                    Diagnostics.ArmorLog.Debug("Skipping due schedule for policy '" + policy.Name + "': a run is already in progress. Will retry.");
                    RecordDecision(TelemetryNames.DecisionAlreadyRunning);
                    counts.Pending++;
                    continue;
                }
                catch (Exception ex)
                {
                    // One policy's failure (for example a remote target that cannot be contacted) must not
                    // abort the tick or starve the other schedules. Leave this one due, but back off before
                    // retrying so a persistent failure is not retried and reported on every tick.
                    RecordDecision(TelemetryNames.DecisionFailed);
                    counts.Pending++;
                    TimeSpan delay = NoteFailure(schedule.Id, nowUtc);
                    Diagnostics.ArmorLog.Debug("Backup for policy '" + policy.Name + "' failed; retrying in " + delay + ".");
                    onError?.Invoke(schedule, ex);
                    continue;
                }

                _Failures.Remove(schedule.Id);
                _Evaluator.MarkRan(schedule, nowUtc);
                await _Context.Database.Schedules.UpdateAsync(schedule, token).ConfigureAwait(false);
                ran += 1;
                RecordDecision(TelemetryNames.DecisionRan);

                // Surface the completed run to the host (the tray agent raises a desktop notification). Kept
                // outside the try above so a reporting hiccup cannot be mistaken for a backup failure.
                try
                {
                    onCompleted?.Invoke(schedule, policy, job);
                }
                catch (Exception)
                {
                    // Reporting a completion must never disrupt the scheduling loop.
                }
            }

            return ran;
        }

        private TimeSpan NoteFailure(string scheduleId, DateTime nowUtc)
        {
            FailureState? state;
            if (!_Failures.TryGetValue(scheduleId, out state))
            {
                state = new FailureState();
                _Failures[scheduleId] = state;
            }
            state.Count++;

            // InitialBackoff doubled per consecutive failure, capped (the exponent is bounded to avoid overflow).
            double factor = Math.Pow(2, Math.Min(state.Count - 1, 16));
            TimeSpan delay = TimeSpan.FromTicks((long)Math.Min(InitialBackoff.Ticks * factor, MaxBackoff.Ticks));
            state.RetryAtUtc = nowUtc + delay;
            return delay;
        }

        private static void RecordDecision(string decision)
        {
            TagList tags = new TagList();
            tags.Add(TelemetryNames.AttrSchedulerDecision, decision);
            ArmorTelemetry.Add(ArmorTelemetry.SchedulerDecisions, 1, tags);
        }

        private sealed class FailureState
        {
            internal int Count { get; set; } = 0;

            internal DateTime RetryAtUtc { get; set; } = DateTime.MinValue;
        }
    }
}
