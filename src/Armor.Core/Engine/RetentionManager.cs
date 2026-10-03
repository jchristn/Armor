namespace Armor.Core.Engine
{
    using System;
    using System.Collections.Generic;
    using System.Diagnostics;
    using System.Threading;
    using System.Threading.Tasks;
    using Armor.Core.ChunkStore;
    using Armor.Core.Database;
    using Armor.Core.Enums;
    using Armor.Core.Models;
    using Armor.Core.Storage;
    using Armor.Core.Telemetry;

    /// <summary>
    /// Applies a policy's retention window: it prunes backup points-in-time older than the window, then
    /// garbage-collects chunks that no surviving manifest references. Pruning decrements the chunk
    /// reference counts contributed by each removed manifest, so a chunk is deleted only after the last
    /// manifest referencing it is gone. The most recent completed point-in-time is always kept, so a
    /// policy never loses its only restore point to age. The invariant is that every surviving
    /// point-in-time still restores after a pass.
    /// </summary>
    public sealed class RetentionManager
    {
        private readonly DatabaseDriverBase _Database;

        /// <summary>
        /// Initializes a new instance of the <see cref="RetentionManager"/> class.
        /// </summary>
        /// <param name="database">The database driver. Cannot be null.</param>
        /// <exception cref="ArgumentNullException">Thrown when <paramref name="database"/> is null.</exception>
        public RetentionManager(DatabaseDriverBase database)
        {
            _Database = database ?? throw new ArgumentNullException(nameof(database));
        }

        /// <summary>
        /// Run retention for a policy.
        /// </summary>
        /// <param name="policy">The policy. Cannot be null.</param>
        /// <param name="repository">The storage repository for the policy's target. Cannot be null.</param>
        /// <param name="storageTargetId">Identifier of the storage target (scopes the chunk index). Cannot be null or whitespace.</param>
        /// <param name="dataKey">The 32-byte repository data key. Cannot be null.</param>
        /// <param name="nowUtc">The current time (injected for testability).</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>The retention result.</returns>
        /// <exception cref="ArgumentNullException">Thrown when a required argument is null.</exception>
        public async Task<RetentionResult> RunAsync(
            Policy policy,
            IStorageRepository repository,
            string storageTargetId,
            byte[] dataKey,
            DateTime nowUtc,
            CancellationToken token = default)
        {
            if (policy == null)
                throw new ArgumentNullException(nameof(policy));
            if (repository == null)
                throw new ArgumentNullException(nameof(repository));
            if (String.IsNullOrWhiteSpace(storageTargetId))
                throw new ArgumentNullException(nameof(storageTargetId));
            if (dataKey == null)
                throw new ArgumentNullException(nameof(dataKey));

            long start = Stopwatch.GetTimestamp();
            Activity? activity = ArmorTelemetry.StartActivity(TelemetryNames.SpanRetentionRun);
            ArmorTelemetry.SetTag(activity, TelemetryNames.AttrPolicyId, policy.Id);
            ArmorTelemetry.SetTag(activity, TelemetryNames.AttrStorageTargetId, storageTargetId);
            ArmorTelemetry.SetTag(activity, "armor.retention.days", policy.RetentionDays);
            string outcome = TelemetryNames.OutcomeFailure;
            string? errorType = null;
            try
            {
                RetentionResult completed = await RunCoreAsync(policy, repository, storageTargetId, dataKey, nowUtc, token).ConfigureAwait(false);
                outcome = TelemetryNames.OutcomeSuccess;
                ArmorTelemetry.SetTag(activity, "armor.retention.jobs_pruned", completed.JobsPruned);
                ArmorTelemetry.SetTag(activity, "armor.retention.chunks_deleted", completed.ChunksDeleted);
                ArmorTelemetry.MarkSuccess(activity);
                return completed;
            }
            catch (Exception ex)
            {
                outcome = ArmorTelemetry.OutcomeOf(ex);
                if (outcome == TelemetryNames.OutcomeFailure)
                {
                    errorType = ArmorTelemetry.ErrorType(ex);
                    ArmorTelemetry.RecordError(TelemetryNames.ComponentRetention, ex);
                }
                ArmorTelemetry.MarkException(activity, ex);
                throw;
            }
            finally
            {
                TagList tags = new TagList();
                tags.Add(TelemetryNames.AttrOutcome, outcome);
                ArmorTelemetry.Record(ArmorTelemetry.RetentionDuration, Stopwatch.GetElapsedTime(start).TotalSeconds, tags);
                if (errorType != null)
                    tags.Add(TelemetryNames.AttrErrorType, errorType);
                ArmorTelemetry.Add(ArmorTelemetry.RetentionRuns, 1, tags);
                ArmorTelemetry.Stop(activity);
            }
        }

        private async Task<RetentionResult> RunCoreAsync(
            Policy policy,
            IStorageRepository repository,
            string storageTargetId,
            byte[] dataKey,
            DateTime nowUtc,
            CancellationToken token)
        {
            RetentionResult result = new RetentionResult();
            DateTime cutoff = nowUtc.AddDays(-policy.RetentionDays);

            List<BackupJob> byPolicy = await _Database.BackupJobs.ReadByPolicyAsync(policy.Id, token).ConfigureAwait(false);
            List<BackupJob> completed = new List<BackupJob>();
            foreach (BackupJob job in byPolicy)
            {
                if (job.Status == JobStatusEnum.Completed)
                    completed.Add(job);
            }

            completed.Sort((left, right) => Nullable.Compare(right.CompletedUtc, left.CompletedUtc));

            using (TelemetryStage pruneStage = StartStage(TelemetryNames.StagePrune))
            {
                try
                {
                    for (int i = 0; i < completed.Count; i++)
                    {
                        token.ThrowIfCancellationRequested();

                        if (i == 0)
                            continue;

                        BackupJob job = completed[i];
                        if (!job.CompletedUtc.HasValue || job.CompletedUtc.Value >= cutoff)
                            continue;

                        await PruneJobAsync(job, storageTargetId, dataKey, repository, token).ConfigureAwait(false);
                        result.JobsPruned += 1;
                        ArmorTelemetry.Add(ArmorTelemetry.RetentionJobsPruned, 1, default(TagList));
                    }
                    pruneStage.Succeed();
                }
                catch (Exception ex)
                {
                    pruneStage.Fail(ex);
                    throw;
                }
            }

            using (TelemetryStage sweepStage = StartStage(TelemetryNames.StageSweep))
            {
                try
                {
                    result.ChunksDeleted = await SweepAsync(storageTargetId, repository, token).ConfigureAwait(false);
                    sweepStage.Succeed();
                }
                catch (Exception ex)
                {
                    sweepStage.Fail(ex);
                    throw;
                }
            }
            return result;
        }

        private static TelemetryStage StartStage(string stage)
        {
            return TelemetryStage.Start(ArmorTelemetry.RetentionStage, ArmorTelemetry.RetentionStageDuration, stage);
        }

        private async Task PruneJobAsync(BackupJob job, string storageTargetId, byte[] dataKey, IStorageRepository repository, CancellationToken token)
        {
            if (!String.IsNullOrEmpty(job.ManifestKey))
            {
                // Stream the manifest to drop a reference for every chunk it used, then delete all of its
                // objects (header plus segments). A manifest that cannot be read is treated as referencing
                // nothing — its chunks are reclaimed later by the unreferenced sweep — and its objects are
                // still deleted.
                try
                {
                    await foreach (ManifestFileEntry entry in ManifestStore.StreamAsync(repository, job.ManifestKey!, job.Id, dataKey, token).ConfigureAwait(false))
                    {
                        foreach (string hash in entry.ChunkHashes)
                            await _Database.ChunkIndex.DecrementReferenceAsync(storageTargetId, hash, token).ConfigureAwait(false);
                    }
                }
                catch (Armor.Core.Exceptions.ArmorException)
                {
                }

                await ManifestStore.DeleteAsync(repository, job.ManifestKey!, job.Id, dataKey, token).ConfigureAwait(false);
            }

            await _Database.BackupJobs.DeleteAsync(job.Id, token).ConfigureAwait(false);
        }

        private async Task<int> SweepAsync(string storageTargetId, IStorageRepository repository, CancellationToken token)
        {
            int deleted = 0;
            List<ChunkIndexEntry> unreferenced = await _Database.ChunkIndex.ReadUnreferencedAsync(storageTargetId, token).ConfigureAwait(false);
            foreach (ChunkIndexEntry entry in unreferenced)
            {
                token.ThrowIfCancellationRequested();
                await repository.DeleteChunkAsync(entry.Hash, token).ConfigureAwait(false);
                await _Database.ChunkIndex.DeleteAsync(storageTargetId, entry.Hash, token).ConfigureAwait(false);
                deleted += 1;
                ArmorTelemetry.Add(ArmorTelemetry.RetentionChunksDeleted, 1, default(TagList));
            }
            return deleted;
        }

    }
}
