namespace Armor.Core.Service
{
    using System;
    using System.Collections.Generic;
    using System.Diagnostics;
    using System.Threading;
    using System.Threading.Tasks;
    using Armor.Core.Enums;
    using Armor.Core.Models;
    using Armor.Core.Scheduling;
    using Armor.Core.Telemetry;

    /// <summary>
    /// One-time housekeeping run when an Armor process starts. Its job is to heal state a previous
    /// process left inconsistent — most importantly backup jobs still marked <see cref="JobStatusEnum.Running"/>
    /// because the process that owned them exited (crashed, was killed, or lost power) before it could
    /// mark them done. Left alone those jobs linger forever as "running", inflating the run count and
    /// skewing success statistics.
    /// </summary>
    public sealed class StartupMaintenance
    {
        private readonly ArmorContext _Context;

        /// <summary>
        /// Initializes a new instance of the <see cref="StartupMaintenance"/> class.
        /// </summary>
        /// <param name="context">The runtime context. Cannot be null.</param>
        /// <exception cref="ArgumentNullException">Thrown when <paramref name="context"/> is null.</exception>
        public StartupMaintenance(ArmorContext context)
        {
            _Context = context ?? throw new ArgumentNullException(nameof(context));
        }

        /// <summary>
        /// Find backup jobs still marked running and, for any whose policy is not actually running now,
        /// mark them failed (interrupted). A job whose policy currently holds the run lock is left alone,
        /// because that lock means a live run — in this or another Armor process — still owns it. Interrupted
        /// jobs keep their work list, so they remain resumable on the policy's next backup.
        /// </summary>
        /// <param name="token">Cancellation token.</param>
        /// <returns>The number of jobs marked interrupted.</returns>
        public async Task<int> ReconcileInterruptedBackupsAsync(CancellationToken token = default)
        {
            Activity? activity = ArmorTelemetry.StartActivity(TelemetryNames.SpanStartupReconcile);
            try
            {
                int reconciled = await ReconcileCoreAsync(token).ConfigureAwait(false);
                if (reconciled > 0)
                    ArmorTelemetry.Add(ArmorTelemetry.StartupReconciledJobs, reconciled, default(TagList));
                ArmorTelemetry.SetTag(activity, "armor.jobs.reconciled", reconciled);
                ArmorTelemetry.MarkSuccess(activity);
                return reconciled;
            }
            catch (Exception ex)
            {
                ArmorTelemetry.MarkException(activity, ex);
                throw;
            }
            finally
            {
                ArmorTelemetry.Stop(activity);
            }
        }

        /// <summary>
        /// Seed the <see cref="TelemetryNames.BackupLastSuccess"/> gauge from the database: the completion time
        /// of the newest successful backup across all policies. Without this the gauge would be absent after a
        /// restart until the next backup succeeds, and a staleness alert could not tell "no backup for days"
        /// from "the process just started". Best-effort: a read failure leaves the gauge unseeded.
        /// </summary>
        /// <param name="token">Cancellation token.</param>
        /// <returns>The completion time that was published, or null when there is no completed backup.</returns>
        public async Task<DateTime?> PublishLastBackupSuccessAsync(CancellationToken token = default)
        {
            try
            {
                DateTime? newest = null;
                List<Policy> policies = await _Context.Database.Policies.ReadAllAsync(token).ConfigureAwait(false);
                foreach (Policy policy in policies)
                {
                    token.ThrowIfCancellationRequested();
                    BackupJob? latest = await _Context.Database.BackupJobs.ReadLatestCompletedAsync(policy.Id, token).ConfigureAwait(false);
                    if (latest?.CompletedUtc != null && (newest == null || latest.CompletedUtc.Value > newest.Value))
                        newest = latest.CompletedUtc.Value;
                }

                if (newest.HasValue)
                    ArmorTelemetry.NoteBackupSucceeded(newest.Value);
                return newest;
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception ex)
            {
                Diagnostics.ArmorLog.Debug("Could not seed the last-backup-success gauge: " + ex.Message);
                return null;
            }
        }

        private async Task<int> ReconcileCoreAsync(CancellationToken token)
        {
            List<BackupJob> jobs = await _Context.Database.BackupJobs.ReadAllAsync(token).ConfigureAwait(false);
            RunLock runLock = new RunLock(_Context.Paths.StateDirectory);
            int reconciled = 0;

            foreach (BackupJob job in jobs)
            {
                token.ThrowIfCancellationRequested();

                if (job.Status != JobStatusEnum.Running)
                    continue;

                // If we can take the policy's run lock, no live run owns this job, so it was orphaned by a
                // process that exited. If we cannot, a run is genuinely in flight — leave the job be. Guard
                // the lock name check so a malformed policy id can never abort startup.
                RunLockHandle? handle;
                try
                {
                    handle = runLock.TryAcquire(job.PolicyId);
                }
                catch (ArgumentException)
                {
                    continue;
                }

                if (handle == null)
                    continue;

                using (handle)
                {
                    job.Status = JobStatusEnum.Failed;
                    job.CompletedUtc = DateTime.UtcNow;
                    if (String.IsNullOrEmpty(job.Error))
                        job.Error = "Interrupted — the Armor process exited before this backup finished.";
                    await _Context.Database.BackupJobs.UpdateAsync(job, token).ConfigureAwait(false);
                    reconciled += 1;
                    Diagnostics.ArmorLog.Warn("Marked interrupted backup job '" + job.Id + "' (policy " + job.PolicyId + ") as failed; its work list is preserved for resume.");
                }
            }

            if (reconciled > 0)
                Diagnostics.ArmorLog.Info("Startup reconciliation marked " + reconciled + " interrupted backup job(s) as failed.");

            return reconciled;
        }
    }
}
