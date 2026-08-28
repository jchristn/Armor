namespace Armor.Core.Engine
{
    using System;
    using System.Collections.Generic;
    using System.Threading;
    using System.Threading.Tasks;
    using Armor.Core.ChunkStore;
    using Armor.Core.Database;
    using Armor.Core.Enums;
    using Armor.Core.Models;
    using Armor.Core.Storage;

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
            }

            result.ChunksDeleted = await SweepAsync(storageTargetId, repository, token).ConfigureAwait(false);
            return result;
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
            }
            return deleted;
        }

    }
}
