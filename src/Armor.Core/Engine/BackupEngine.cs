namespace Armor.Core.Engine
{
    using System;
    using System.Collections.Generic;
    using System.IO;
    using System.Text;
    using System.Threading;
    using System.Threading.Tasks;
    using Armor.Core.ChunkStore;
    using Armor.Core.Configuration;
    using Armor.Core.Database;
    using Armor.Core.Enums;
    using Armor.Core.Models;
    using Armor.Core.Storage;

    /// <summary>
    /// Executes a backup run for a policy: it enumerates included files, decides which need
    /// re-chunking against the baseline point-in-time, writes new chunks (skipping duplicates already
    /// on the target), and records a manifest that lists every file with its ordered chunk references.
    /// The repository header is refreshed so the target alone can drive disaster recovery. Runs are
    /// cancellable and update the backup-job row as they progress.
    /// </summary>
    public sealed class BackupEngine
    {
        private readonly DatabaseDriverBase _Database;
        private readonly FileEnumerator _Enumerator = new FileEnumerator();
        private readonly ChangeDetector _ChangeDetector = new ChangeDetector();

        /// <summary>
        /// Initializes a new instance of the <see cref="BackupEngine"/> class.
        /// </summary>
        /// <param name="database">The database driver. Cannot be null.</param>
        /// <exception cref="ArgumentNullException">Thrown when <paramref name="database"/> is null.</exception>
        public BackupEngine(DatabaseDriverBase database)
        {
            _Database = database ?? throw new ArgumentNullException(nameof(database));
        }

        /// <summary>
        /// Run a backup for a policy.
        /// </summary>
        /// <param name="policy">The policy to run. Cannot be null.</param>
        /// <param name="repository">The storage repository for the policy's target. Cannot be null.</param>
        /// <param name="storageTargetId">Identifier of the storage target (scopes the chunk index). Cannot be null or whitespace.</param>
        /// <param name="encryptionKey">The encryption-key entry (written into the repository header). Cannot be null.</param>
        /// <param name="dataKey">The 32-byte repository data key. Cannot be null.</param>
        /// <param name="chunking">Chunking parameters. Cannot be null.</param>
        /// <param name="backupTypeOverride">Optional backup type overriding the policy's configured type.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>The completed backup-job record.</returns>
        /// <exception cref="ArgumentNullException">Thrown when a required argument is null.</exception>
        public async Task<BackupJob> RunAsync(
            Policy policy,
            IStorageRepository repository,
            string storageTargetId,
            EncryptionKey encryptionKey,
            byte[] dataKey,
            ChunkingSettings chunking,
            BackupTypeEnum? backupTypeOverride = null,
            CancellationToken token = default)
        {
            if (policy == null)
                throw new ArgumentNullException(nameof(policy));
            if (repository == null)
                throw new ArgumentNullException(nameof(repository));
            if (String.IsNullOrWhiteSpace(storageTargetId))
                throw new ArgumentNullException(nameof(storageTargetId));
            if (encryptionKey == null)
                throw new ArgumentNullException(nameof(encryptionKey));
            if (dataKey == null)
                throw new ArgumentNullException(nameof(dataKey));
            if (chunking == null)
                throw new ArgumentNullException(nameof(chunking));

            BackupTypeEnum backupType = backupTypeOverride ?? policy.BackupType;

            BackupJob job = new BackupJob();
            job.PolicyId = policy.Id;
            job.BackupType = backupType;
            job.Status = JobStatusEnum.Running;
            job.StartedUtc = DateTime.UtcNow;

            BackupJob? baselineJob = await ResolveBaselineAsync(policy.Id, backupType, token).ConfigureAwait(false);
            job.BaseJobId = baselineJob?.Id;
            await _Database.BackupJobs.CreateAsync(job, token).ConfigureAwait(false);

            try
            {
                await WriteHeaderAsync(repository, encryptionKey, chunking, token).ConfigureAwait(false);
                await _Database.PolicyState.EnsureTableAsync(policy.Id, token).ConfigureAwait(false);

                Dictionary<string, ManifestFileEntry> baseline = await LoadBaselineEntriesAsync(repository, baselineJob, dataKey, token).ConfigureAwait(false);

                Manifest manifest = new Manifest();
                manifest.JobId = job.Id;
                manifest.PolicyId = policy.Id;
                manifest.BackupType = backupType;
                manifest.BaseJobId = baselineJob?.Id;
                manifest.PointInTimeUtc = job.StartedUtc ?? DateTime.UtcNow;

                FastCdc chunker = new FastCdc(chunking);

                await foreach (string path in _Enumerator.EnumerateAsync(policy, token).ConfigureAwait(false))
                {
                    token.ThrowIfCancellationRequested();

                    FileInfo info = new FileInfo(path);
                    if (!info.Exists)
                        continue;

                    ManifestFileEntry? baselineEntry = null;
                    baseline.TryGetValue(path, out baselineEntry);

                    bool reuse = backupType != BackupTypeEnum.Full
                        && baselineEntry != null
                        && !_ChangeDetector.HasChanged(info, baselineEntry, policy.UseArchiveBit);

                    ManifestFileEntry entry = new ManifestFileEntry();
                    entry.Path = path;
                    entry.SizeBytes = info.Length;
                    entry.ModifiedUtc = info.LastWriteTimeUtc;
                    entry.ArchiveBit = _ChangeDetector.IsArchiveBitSet(path);

                    if (reuse && baselineEntry != null)
                    {
                        foreach (string hash in baselineEntry.ChunkHashes)
                        {
                            entry.ChunkHashes.Add(hash);
                            await _Database.ChunkIndex.IncrementReferenceAsync(storageTargetId, hash, token).ConfigureAwait(false);
                        }
                        job.ChunksReused += baselineEntry.ChunkHashes.Count;
                        job.BytesDeduplicated += info.Length;
                    }
                    else
                    {
                        await ChunkFileAsync(path, chunker, repository, storageTargetId, dataKey, entry, job, token).ConfigureAwait(false);
                        if (policy.UseArchiveBit)
                            _ChangeDetector.ClearArchiveBit(path);
                    }

                    manifest.Files.Add(entry);
                    job.FileCount += 1;
                    job.BytesTotal += info.Length;

                    await UpdatePolicyStateAsync(policy.Id, entry, job.Id, token).ConfigureAwait(false);
                }

                string manifestKey = RepositoryKeys.ManifestKey(policy.Id, job.Id);
                byte[] manifestBytes = ManifestCodec.Encode(manifest, dataKey);
                await repository.WriteObjectAsync(manifestKey, manifestBytes, token).ConfigureAwait(false);

                // Write a small encrypted metadata sidecar so the catalog can be listed and described
                // during recovery without decoding the full manifest.
                BackupRunInfo runInfo = new BackupRunInfo();
                runInfo.JobId = job.Id;
                runInfo.PolicyId = policy.Id;
                runInfo.PolicyName = policy.Name;
                runInfo.BackupType = manifest.BackupType;
                runInfo.PointInTimeUtc = manifest.PointInTimeUtc;
                runInfo.FileCount = job.FileCount;
                runInfo.TotalBytes = job.BytesTotal;
                runInfo.BytesWritten = job.BytesWritten;
                runInfo.ChunksWritten = job.ChunksWritten;
                await repository.WriteObjectAsync(RepositoryKeys.InfoKey(policy.Id, job.Id), RunInfoCodec.Encode(runInfo, dataKey), token).ConfigureAwait(false);

                job.ManifestKey = manifestKey;
                job.Status = JobStatusEnum.Completed;
                job.CompletedUtc = DateTime.UtcNow;
                await _Database.BackupJobs.UpdateAsync(job, token).ConfigureAwait(false);
                return job;
            }
            catch (OperationCanceledException)
            {
                job.Status = JobStatusEnum.Canceled;
                job.CompletedUtc = DateTime.UtcNow;
                await _Database.BackupJobs.UpdateAsync(job, CancellationToken.None).ConfigureAwait(false);
                throw;
            }
            catch (Exception ex)
            {
                job.Status = JobStatusEnum.Failed;
                job.Error = ex.Message;
                job.CompletedUtc = DateTime.UtcNow;
                await _Database.BackupJobs.UpdateAsync(job, CancellationToken.None).ConfigureAwait(false);
                throw;
            }
        }

        private async Task ChunkFileAsync(
            string path,
            FastCdc chunker,
            IStorageRepository repository,
            string storageTargetId,
            byte[] dataKey,
            ManifestFileEntry entry,
            BackupJob job,
            CancellationToken token)
        {
            using (FileStream stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read))
            {
                await foreach (byte[] chunk in chunker.ChunkAsync(stream, token).ConfigureAwait(false))
                {
                    string hash = Hasher.Sha256Hex(chunk);
                    entry.ChunkHashes.Add(hash);

                    bool exists = await _Database.ChunkIndex.ExistsAsync(storageTargetId, hash, token).ConfigureAwait(false);
                    if (exists)
                    {
                        await _Database.ChunkIndex.IncrementReferenceAsync(storageTargetId, hash, token).ConfigureAwait(false);
                        job.ChunksReused += 1;
                        job.BytesDeduplicated += chunk.Length;
                        continue;
                    }

                    byte[] stored = ChunkFramer.Frame(chunk, dataKey, hash);
                    await repository.WriteChunkAsync(hash, stored, token).ConfigureAwait(false);

                    ChunkIndexEntry indexEntry = new ChunkIndexEntry();
                    indexEntry.StorageTargetId = storageTargetId;
                    indexEntry.Hash = hash;
                    indexEntry.StoredSizeBytes = stored.Length;
                    indexEntry.PlaintextSizeBytes = chunk.Length;
                    await _Database.ChunkIndex.AddOrReferenceAsync(indexEntry, token).ConfigureAwait(false);

                    job.ChunksWritten += 1;
                    job.BytesWritten += stored.Length;
                }
            }
        }

        private async Task<BackupJob?> ResolveBaselineAsync(string policyId, BackupTypeEnum backupType, CancellationToken token)
        {
            if (backupType == BackupTypeEnum.Incremental)
                return await _Database.BackupJobs.ReadLatestCompletedAsync(policyId, token).ConfigureAwait(false);
            if (backupType == BackupTypeEnum.Differential)
                return await _Database.BackupJobs.ReadLatestCompletedFullAsync(policyId, token).ConfigureAwait(false);
            return null;
        }

        private static async Task<Dictionary<string, ManifestFileEntry>> LoadBaselineEntriesAsync(
            IStorageRepository repository,
            BackupJob? baselineJob,
            byte[] dataKey,
            CancellationToken token)
        {
            Dictionary<string, ManifestFileEntry> map = new Dictionary<string, ManifestFileEntry>(StringComparer.Ordinal);
            if (baselineJob == null || String.IsNullOrEmpty(baselineJob.ManifestKey))
                return map;

            byte[] bytes = await repository.ReadObjectAsync(baselineJob.ManifestKey!, token).ConfigureAwait(false);
            Manifest manifest = ManifestCodec.Decode(bytes, dataKey, baselineJob.Id);
            foreach (ManifestFileEntry entry in manifest.Files)
                map[entry.Path] = entry;
            return map;
        }

        private static async Task WriteHeaderAsync(IStorageRepository repository, EncryptionKey encryptionKey, ChunkingSettings chunking, CancellationToken token)
        {
            RepositoryHeader header = RepositoryHeader.FromEncryptionKey(encryptionKey, chunking);
            byte[] bytes = Encoding.UTF8.GetBytes(Serialization.ArmorJson.Serialize(header));
            await repository.WriteObjectAsync(RepositoryKeys.HeaderKey, bytes, token).ConfigureAwait(false);
        }

        private async Task UpdatePolicyStateAsync(string policyId, ManifestFileEntry entry, string jobId, CancellationToken token)
        {
            PolicyStateEntry state = new PolicyStateEntry();
            state.Path = entry.Path;
            state.SizeBytes = entry.SizeBytes;
            state.ModifiedUtc = entry.ModifiedUtc;
            state.ArchiveBit = entry.ArchiveBit;
            state.ChunkListHash = Hasher.Sha256HexOfText(String.Join("\n", entry.ChunkHashes));
            state.LastJobId = jobId;
            state.UpdatedUtc = DateTime.UtcNow;
            await _Database.PolicyState.UpsertAsync(policyId, state, token).ConfigureAwait(false);
        }
    }
}
