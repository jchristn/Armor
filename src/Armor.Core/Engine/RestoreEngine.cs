namespace Armor.Core.Engine
{
    using System;
    using System.Collections.Generic;
    using System.Diagnostics;
    using System.IO;
    using System.Threading;
    using System.Threading.Tasks;
    using Armor.Core.ChunkStore;
    using Armor.Core.Database;
    using Armor.Core.Enums;
    using Armor.Core.Exceptions;
    using Armor.Core.Helpers;
    using Armor.Core.Models;
    using Armor.Core.Storage;
    using Armor.Core.Telemetry;

    /// <summary>
    /// Reconstructs files from a backup point-in-time. A restore reads exactly one manifest, selects
    /// the requested scope, and rebuilds each file by fetching, decrypting, and verifying its chunks in
    /// order. Because every chunk is authenticated against its content hash, a corrupt or missing chunk
    /// aborts the restore rather than producing wrong output. A standalone verify walks a manifest
    /// without writing files.
    /// </summary>
    public sealed class RestoreEngine
    {
        private readonly DatabaseDriverBase _Database;

        /// <summary>
        /// Initializes a new instance of the <see cref="RestoreEngine"/> class.
        /// </summary>
        /// <param name="database">The database driver. Cannot be null.</param>
        /// <exception cref="ArgumentNullException">Thrown when <paramref name="database"/> is null.</exception>
        public RestoreEngine(DatabaseDriverBase database)
        {
            _Database = database ?? throw new ArgumentNullException(nameof(database));
        }

        /// <summary>
        /// Run a restore for a backup point-in-time.
        /// </summary>
        /// <param name="restoreJob">The restore job describing scope and destination. Cannot be null.</param>
        /// <param name="backupJob">The backup job (point-in-time) to restore. Cannot be null.</param>
        /// <param name="repository">The storage repository for the target. Cannot be null.</param>
        /// <param name="dataKey">The 32-byte repository data key. Cannot be null.</param>
        /// <param name="token">Cancellation token.</param>
        /// <param name="progress">Optional observer notified as files are written. Totals are fixed from
        /// the backup point-in-time's record, so the first report already carries the final totals.</param>
        /// <returns>The completed restore-job record.</returns>
        /// <exception cref="ArgumentNullException">Thrown when a required argument is null.</exception>
        /// <exception cref="ArmorException">Thrown when the backup job has no manifest.</exception>
        public async Task<RestoreJob> RunAsync(
            RestoreJob restoreJob,
            BackupJob backupJob,
            IStorageRepository repository,
            byte[] dataKey,
            CancellationToken token = default,
            IProgress<RestoreProgress>? progress = null)
        {
            if (restoreJob == null)
                throw new ArgumentNullException(nameof(restoreJob));
            if (backupJob == null)
                throw new ArgumentNullException(nameof(backupJob));
            if (repository == null)
                throw new ArgumentNullException(nameof(repository));
            if (dataKey == null)
                throw new ArgumentNullException(nameof(dataKey));
            if (String.IsNullOrEmpty(backupJob.ManifestKey))
                throw new ArmorException("Backup job '" + backupJob.Id + "' has no manifest to restore from.");

            long start = Stopwatch.GetTimestamp();
            Activity? activity = ArmorTelemetry.StartActivity(TelemetryNames.SpanRestoreJob);
            ArmorTelemetry.SetTag(activity, TelemetryNames.AttrJobId, backupJob.Id);
            ArmorTelemetry.SetTag(activity, TelemetryNames.AttrPolicyId, backupJob.PolicyId);
            ArmorTelemetry.SetTag(activity, "armor.restore.scope", restoreJob.Scope.ToString());
            ArmorTelemetry.Add(ArmorTelemetry.RestoreJobsActive, 1, default(TagList));
            string outcome = TelemetryNames.OutcomeFailure;
            string? errorType = null;
            try
            {
                RestoreJob completed = await RunJobAsync(restoreJob, backupJob, repository, dataKey, token, progress).ConfigureAwait(false);
                outcome = TelemetryNames.OutcomeSuccess;
                ArmorTelemetry.SetTag(activity, TelemetryNames.AttrRestoreJobId, completed.Id);
                ArmorTelemetry.SetTag(activity, TelemetryNames.AttrFileCount, completed.FilesRestored);
                ArmorTelemetry.SetTag(activity, TelemetryNames.AttrByteCount, completed.BytesRestored);
                ArmorTelemetry.MarkSuccess(activity);
                return completed;
            }
            catch (Exception ex)
            {
                outcome = ArmorTelemetry.OutcomeOf(ex);
                if (outcome == TelemetryNames.OutcomeFailure)
                {
                    errorType = ArmorTelemetry.ErrorType(ex);
                    ArmorTelemetry.RecordError(TelemetryNames.ComponentRestore, ex);
                }
                ArmorTelemetry.MarkException(activity, ex);
                throw;
            }
            finally
            {
                ArmorTelemetry.Add(ArmorTelemetry.RestoreJobsActive, -1, default(TagList));
                TagList tags = new TagList();
                tags.Add(TelemetryNames.AttrOutcome, outcome);
                ArmorTelemetry.Record(ArmorTelemetry.RestoreDuration, Stopwatch.GetElapsedTime(start).TotalSeconds, tags);
                if (errorType != null)
                    tags.Add(TelemetryNames.AttrErrorType, errorType);
                ArmorTelemetry.Add(ArmorTelemetry.RestoreJobs, 1, tags);
                ArmorTelemetry.Stop(activity);
            }
        }

        private async Task<RestoreJob> RunJobAsync(
            RestoreJob restoreJob,
            BackupJob backupJob,
            IStorageRepository repository,
            byte[] dataKey,
            CancellationToken token,
            IProgress<RestoreProgress>? progress)
        {
            using (TelemetryStage openStage = StartStage(TelemetryNames.StageOpen))
            {
                try
                {
                    restoreJob.Status = JobStatusEnum.Running;
                    restoreJob.StartedUtc = DateTime.UtcNow;
                    await _Database.RestoreJobs.CreateAsync(restoreJob, token).ConfigureAwait(false);
                    openStage.Succeed();
                }
                catch (Exception ex)
                {
                    openStage.Fail(ex);
                    throw;
                }
            }

            try
            {
                string? normalizedSelector = String.IsNullOrEmpty(restoreJob.SourceSelector) ? null : Normalize(restoreJob.SourceSelector);

                // Totals come from the backup point-in-time's record, so an observer can render a real
                // completion fraction from the first file (a whole-backup restore hits 100%; a scoped
                // restore reports against the full manifest, so it simply stops short of 100%). Emit an
                // initial zero-progress report up front so the UI shows the bar and totals immediately.
                int filesTotal = backupJob.FileCount > int.MaxValue ? int.MaxValue : (int)backupJob.FileCount;
                long bytesTotal = backupJob.BytesTotal;
                progress?.Report(new RestoreProgress { FilesTotal = filesTotal, BytesTotal = bytesTotal, FilesDone = 0, BytesDone = 0 });

                TelemetryStage restoreStage = StartStage(TelemetryNames.StageRestore);
                try
                {
                    // Stream the manifest one segment at a time and restore each in-scope file as it arrives, so a
                    // restore never materializes the whole file list in memory.
                    await foreach (ManifestFileEntry entry in ManifestStore.StreamAsync(repository, backupJob.ManifestKey!, backupJob.Id, dataKey, token).ConfigureAwait(false))
                    {
                        token.ThrowIfCancellationRequested();
                        if (!MatchesScope(entry, restoreJob.Scope, normalizedSelector))
                            continue;
                        string destination = RestorePathMapper.MapDestination(entry.Path, restoreJob.DestinationRoot);
                        await RestoreFileAsync(entry, destination, repository, dataKey, token).ConfigureAwait(false);
                        restoreJob.FilesRestored += 1;
                        restoreJob.BytesRestored += entry.SizeBytes;
                        ArmorTelemetry.Add(ArmorTelemetry.RestoreFiles, 1, default(TagList));
                        ArmorTelemetry.Add(ArmorTelemetry.RestoreBytes, entry.SizeBytes, default(TagList));
                        progress?.Report(new RestoreProgress
                        {
                            FilesTotal = filesTotal,
                            BytesTotal = bytesTotal,
                            FilesDone = restoreJob.FilesRestored > int.MaxValue ? int.MaxValue : (int)restoreJob.FilesRestored,
                            BytesDone = restoreJob.BytesRestored,
                            CurrentPath = entry.Path,
                        });
                    }
                    restoreStage.Succeed();
                }
                catch (Exception ex)
                {
                    restoreStage.Fail(ex);
                    throw;
                }
                finally
                {
                    restoreStage.Dispose();
                }

                using (TelemetryStage finalizeStage = StartStage(TelemetryNames.StageFinalize))
                {
                    try
                    {
                        restoreJob.Status = JobStatusEnum.Completed;
                        restoreJob.CompletedUtc = DateTime.UtcNow;
                        await _Database.RestoreJobs.UpdateAsync(restoreJob, token).ConfigureAwait(false);
                        finalizeStage.Succeed();
                    }
                    catch (Exception ex)
                    {
                        finalizeStage.Fail(ex);
                        throw;
                    }
                }
                return restoreJob;
            }
            catch (OperationCanceledException)
            {
                restoreJob.Status = JobStatusEnum.Canceled;
                restoreJob.CompletedUtc = DateTime.UtcNow;
                await _Database.RestoreJobs.UpdateAsync(restoreJob, CancellationToken.None).ConfigureAwait(false);
                throw;
            }
            catch (Exception ex)
            {
                restoreJob.Status = JobStatusEnum.Failed;
                restoreJob.Error = ex.Message;
                restoreJob.CompletedUtc = DateTime.UtcNow;
                await _Database.RestoreJobs.UpdateAsync(restoreJob, CancellationToken.None).ConfigureAwait(false);
                throw;
            }
        }

        /// <summary>
        /// Verify a backup point-in-time by fetching and authenticating every chunk referenced by its
        /// manifest, without writing any files.
        /// </summary>
        /// <param name="backupJob">The backup job to verify. Cannot be null.</param>
        /// <param name="repository">The storage repository for the target. Cannot be null.</param>
        /// <param name="dataKey">The 32-byte repository data key. Cannot be null.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>The number of chunk references verified.</returns>
        /// <exception cref="ArgumentNullException">Thrown when a required argument is null.</exception>
        /// <exception cref="ArmorException">Thrown when the backup job has no manifest.</exception>
        /// <exception cref="ArmorStorageException">Thrown when a referenced chunk is missing.</exception>
        /// <exception cref="ArmorCryptoException">Thrown when a referenced chunk fails authentication.</exception>
        public async Task<long> VerifyAsync(BackupJob backupJob, IStorageRepository repository, byte[] dataKey, CancellationToken token = default)
        {
            if (backupJob == null)
                throw new ArgumentNullException(nameof(backupJob));
            if (repository == null)
                throw new ArgumentNullException(nameof(repository));
            if (dataKey == null)
                throw new ArgumentNullException(nameof(dataKey));
            if (String.IsNullOrEmpty(backupJob.ManifestKey))
                throw new ArmorException("Backup job '" + backupJob.Id + "' has no manifest to verify.");

            long start = Stopwatch.GetTimestamp();
            Activity? activity = ArmorTelemetry.StartActivity(TelemetryNames.SpanVerifyJob);
            ArmorTelemetry.SetTag(activity, TelemetryNames.AttrJobId, backupJob.Id);
            ArmorTelemetry.SetTag(activity, TelemetryNames.AttrPolicyId, backupJob.PolicyId);
            string outcome = TelemetryNames.OutcomeFailure;
            string? errorType = null;
            try
            {
                long count = await VerifyChunksAsync(backupJob, repository, dataKey, token).ConfigureAwait(false);
                outcome = TelemetryNames.OutcomeSuccess;
                ArmorTelemetry.SetTag(activity, "armor.chunks.verified", count);
                ArmorTelemetry.MarkSuccess(activity);
                return count;
            }
            catch (Exception ex)
            {
                outcome = ArmorTelemetry.OutcomeOf(ex);
                if (outcome == TelemetryNames.OutcomeFailure)
                {
                    errorType = ArmorTelemetry.ErrorType(ex);
                    ArmorTelemetry.RecordError(TelemetryNames.ComponentVerify, ex);
                }
                ArmorTelemetry.MarkException(activity, ex);
                throw;
            }
            finally
            {
                TagList tags = new TagList();
                tags.Add(TelemetryNames.AttrOutcome, outcome);
                ArmorTelemetry.Record(ArmorTelemetry.VerifyDuration, Stopwatch.GetElapsedTime(start).TotalSeconds, tags);
                if (errorType != null)
                    tags.Add(TelemetryNames.AttrErrorType, errorType);
                ArmorTelemetry.Add(ArmorTelemetry.VerifyRuns, 1, tags);
                ArmorTelemetry.Stop(activity);
            }
        }

        private static async Task<long> VerifyChunksAsync(BackupJob backupJob, IStorageRepository repository, byte[] dataKey, CancellationToken token)
        {
            long verified = 0;
            await foreach (ManifestFileEntry entry in ManifestStore.StreamAsync(repository, backupJob.ManifestKey!, backupJob.Id, dataKey, token).ConfigureAwait(false))
            {
                foreach (string hash in entry.ChunkHashes)
                {
                    token.ThrowIfCancellationRequested();
                    bool exists = await repository.ChunkExistsAsync(hash, token).ConfigureAwait(false);
                    if (!exists)
                    {
                        RecordVerifiedChunk(TelemetryNames.ChunkMissing);
                        throw new ArmorStorageException("Chunk '" + hash + "' referenced by file '" + entry.Path + "' is missing from the target.");
                    }

                    byte[] stored = await repository.ReadChunkAsync(hash, token).ConfigureAwait(false);
                    try
                    {
                        ChunkFramer.Unframe(stored, dataKey, hash);
                    }
                    catch (ArmorException)
                    {
                        RecordVerifiedChunk(TelemetryNames.ChunkCorrupt);
                        throw;
                    }
                    RecordVerifiedChunk(TelemetryNames.ChunkVerified);
                    verified += 1;
                }
            }

            return verified;
        }

        /// <summary>Whether a file entry falls within the requested restore scope. The selector is expected pre-normalized (forward slashes).</summary>
        private static void RecordVerifiedChunk(string outcome)
        {
            TagList tags = new TagList();
            tags.Add(TelemetryNames.AttrChunkOutcome, outcome);
            ArmorTelemetry.Add(ArmorTelemetry.VerifyChunks, 1, tags);
        }

        private static TelemetryStage StartStage(string stage)
        {
            return TelemetryStage.Start(ArmorTelemetry.RestoreStage, ArmorTelemetry.RestoreStageDuration, stage);
        }

        private static bool MatchesScope(ManifestFileEntry entry, RestoreScopeEnum scope, string? normalizedSelector)
        {
            if (scope == RestoreScopeEnum.All)
                return true;
            if (String.IsNullOrEmpty(normalizedSelector))
                return false;

            string normalizedPath = Normalize(entry.Path);
            if (scope == RestoreScopeEnum.File)
                return String.Equals(normalizedPath, normalizedSelector, StringComparison.Ordinal);

            string prefix = normalizedSelector!.EndsWith("/", StringComparison.Ordinal) ? normalizedSelector! : normalizedSelector + "/";
            return normalizedPath.StartsWith(prefix, StringComparison.Ordinal) || String.Equals(normalizedPath, normalizedSelector, StringComparison.Ordinal);
        }

        private static async Task RestoreFileAsync(ManifestFileEntry entry, string destination, IStorageRepository repository, byte[] dataKey, CancellationToken token)
        {
            string? directory = Path.GetDirectoryName(destination);
            if (!String.IsNullOrEmpty(directory))
                Directory.CreateDirectory(directory);

            using (FileStream output = new FileStream(destination, FileMode.Create, FileAccess.Write, FileShare.None))
            {
                foreach (string hash in entry.ChunkHashes)
                {
                    token.ThrowIfCancellationRequested();
                    long downloadStart = Stopwatch.GetTimestamp();
                    byte[] stored = await repository.ReadChunkAsync(hash, token).ConfigureAwait(false);
                    ArmorTelemetry.RecordSince(ArmorTelemetry.RestoreFileStageDuration, downloadStart, TelemetryNames.FileStageDownload);
                    long decryptStart = Stopwatch.GetTimestamp();
                    byte[] plaintext = ChunkFramer.Unframe(stored, dataKey, hash);
                    ArmorTelemetry.RecordSince(ArmorTelemetry.RestoreFileStageDuration, decryptStart, TelemetryNames.FileStageDecrypt);
                    long writeStart = Stopwatch.GetTimestamp();
                    await output.WriteAsync(plaintext.AsMemory(0, plaintext.Length), token).ConfigureAwait(false);
                    ArmorTelemetry.RecordSince(ArmorTelemetry.RestoreFileStageDuration, writeStart, TelemetryNames.FileStageWrite);
                }
            }

            if (entry.ModifiedUtc > DateTime.MinValue)
            {
                try
                {
                    File.SetLastWriteTimeUtc(destination, entry.ModifiedUtc);
                }
                catch (IOException)
                {
                }
            }
        }

        private static string Normalize(string path)
        {
            return path.Replace('\\', '/');
        }
    }
}
