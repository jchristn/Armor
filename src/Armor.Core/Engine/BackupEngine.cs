namespace Armor.Core.Engine
{
    using System;
    using System.Collections.Concurrent;
    using System.Collections.Generic;
    using System.IO;
    using System.Text;
    using System.Threading;
    using System.Threading.Channels;
    using System.Threading.Tasks;
    using Armor.Core.ChunkStore;
    using Armor.Core.Configuration;
    using Armor.Core.Database;
    using Armor.Core.Enums;
    using Armor.Core.Exceptions;
    using Armor.Core.Models;
    using Armor.Core.Serialization;
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
        private const int ScanBatchSize = 500;
        private const int ProcessPageSize = 500;

        // How long the work-list producer waits before re-checking for newly scanned rows when the list has
        // temporarily drained but the scan is still running.
        private const int ScanPollMilliseconds = 50;
        private const int ProgressPersistMilliseconds = 2000;

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
        /// <param name="progress">Optional observer notified as files are processed. When supplied, a metadata pre-scan runs first to establish totals.</param>
        /// <param name="maxParallelism">How many files to process at once. Values below 1 are treated as 1
        /// (fully serial); higher values parallelize hashing, compression and encryption across cores. Clamped
        /// to a sane upper bound.</param>
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
            CancellationToken token = default,
            IProgress<BackupProgress>? progress = null,
            int maxParallelism = 1)
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

            // Resume a prior run for this policy that crashed or failed with work still pending. The run
            // lock held by the caller means no other live process owns this policy, so a Running job found
            // here belongs to a dead run. A resumed run keeps the same job id and baseline and skips the
            // files it already finished.
            BackupJob? resumable = await FindResumableJobAsync(policy.Id, backupType, token).ConfigureAwait(false);

            BackupJob job;
            bool resuming;
            if (resumable != null)
            {
                job = resumable;
                job.Status = JobStatusEnum.Running;
                job.Error = null;

                // A resumable run whose scan finished has a complete, trustworthy work list — process only.
                // One that died mid-scan has a partial list that would silently drop unscanned files, so
                // discard it and re-scan from scratch under the same job id and baseline. Chunks already
                // written are content-addressed and dedupe on the rewrite, so the only cost is re-hashing the
                // files that were processed before the crash.
                if (job.ScanComplete)
                {
                    resuming = true;
                }
                else
                {
                    await _Database.JobFiles.DeleteByJobAsync(job.Id, token).ConfigureAwait(false);
                    resuming = false;
                }
                await _Database.BackupJobs.UpdateAsync(job, token).ConfigureAwait(false);
            }
            else
            {
                job = new BackupJob();
                job.PolicyId = policy.Id;
                job.BackupType = backupType;
                job.Status = JobStatusEnum.Running;
                job.StartedUtc = DateTime.UtcNow;
                job.ScanComplete = false;
                BackupJob? freshBaseline = await ResolveBaselineAsync(policy.Id, backupType, token).ConfigureAwait(false);
                job.BaseJobId = freshBaseline?.Id;
                await _Database.BackupJobs.CreateAsync(job, token).ConfigureAwait(false);
                resuming = false;
            }

            try
            {
                await WriteHeaderAsync(repository, encryptionKey, chunking, token).ConfigureAwait(false);

                BackupJob? baselineJob = String.IsNullOrEmpty(job.BaseJobId)
                    ? null
                    : await _Database.BackupJobs.ReadAsync(job.BaseJobId!, token).ConfigureAwait(false);
                Dictionary<string, ManifestFileEntry> baseline = await LoadBaselineEntriesAsync(repository, baselineJob, dataKey, token).ConfigureAwait(false);

                // Phases 1 and 2 run concurrently. The scanner streams the source into the work list in
                // batches while a pool of workers drains the list and backs the files up, so processing starts
                // as soon as the first batch lands instead of waiting for a multi-million-file scan to finish.
                // A resume whose scan already completed skips scanning and just drains the existing list. The
                // ScanCoordinator carries the live scanned totals and the "scan finished" signal that lets the
                // work-list producer tell "temporarily drained" from "truly done".
                JobFileTotals totals = await _Database.JobFiles.ReadTotalsAsync(job.Id, token).ConfigureAwait(false);
                ScanCoordinator scan = new ScanCoordinator();

                long skippedFiles;
                if (resuming)
                {
                    scan.SetTotals(totals.FileCount, totals.TotalBytes);
                    scan.MarkComplete();
                    skippedFiles = await ProcessPendingFilesAsync(
                        job, policy, repository, storageTargetId, dataKey, chunking, baseline, backupType, totals, scan, maxParallelism, progress, token).ConfigureAwait(false);
                }
                else
                {
                    // The files a run keeps out are the policy's own exclude rules plus — when the policy opts
                    // in — the shared global exclude list (build output, package caches, AppData, and the like).
                    // Both are name/path rules that only ever remove files, so their union needs no ordering.
                    List<ExcludePattern> effectiveExcludes = new List<ExcludePattern>(policy.ExcludePatterns);
                    if (policy.UseGlobalExcludes)
                        effectiveExcludes.AddRange(await _Database.GlobalExcludes.ReadAllAsync(token).ConfigureAwait(false));
                    ExcludeMatcher matcher = new ExcludeMatcher(effectiveExcludes);

                    using (CancellationTokenSource scanFailure = CancellationTokenSource.CreateLinkedTokenSource(token))
                    {
                        Exception? scanError = null;
                        Task scanTask = Task.Run(async () =>
                        {
                            try
                            {
                                await ScanIntoWorkListAsync(job, policy, matcher, scan, progress, scanFailure.Token).ConfigureAwait(false);
                            }
                            catch (OperationCanceledException)
                            {
                                // A cancel (user stop, or the processor aborting) ends the wait; let the
                                // processor's own outcome decide cancel-versus-fail.
                                scan.MarkComplete();
                                throw;
                            }
                            catch (Exception ex)
                            {
                                // A scan failure must sink the run rather than back up a partial list: record
                                // it, release the producer, and cancel the workers.
                                scanError = ex;
                                scan.MarkComplete();
                                scanFailure.Cancel();
                                throw;
                            }
                        }, scanFailure.Token);

                        try
                        {
                            skippedFiles = await ProcessPendingFilesAsync(
                                job, policy, repository, storageTargetId, dataKey, chunking, baseline, backupType, totals, scan, maxParallelism, progress, scanFailure.Token).ConfigureAwait(false);
                        }
                        catch (Exception) when (scanError != null)
                        {
                            // The processor stopped because the scan failed first; observe the scan task and
                            // surface the scan's error as the run's failure.
                            await SwallowAsync(scanTask).ConfigureAwait(false);
                            System.Runtime.ExceptionServices.ExceptionDispatchInfo.Capture(scanError).Throw();
                            throw; // unreachable.
                        }
                        catch
                        {
                            // The processor failed on its own; stop the scanner and let its error propagate.
                            scanFailure.Cancel();
                            await SwallowAsync(scanTask).ConfigureAwait(false);
                            throw;
                        }

                        await scanTask.ConfigureAwait(false);
                    }
                }

                if (skippedFiles > 0)
                    Diagnostics.ArmorLog.Warn("Backup of policy '" + policy.Name + "' skipped " + skippedFiles + " unreadable file(s); see earlier warnings for paths.");

                // Phase 3 — assemble the manifest from the completed work list (read in pages) and stream it
                // to the target as a header plus numbered segment objects, then write the metadata sidecar and
                // delete the work list. The manifest is never held in memory in full: each done-page is turned
                // into entries and handed to the writer, which flushes a bounded segment at a time. This keeps
                // a multi-million-file manifest off the heap and away from the ~2 GB single-object ceiling that
                // a one-shot serialize-and-encrypt would hit.
                DateTime pointInTime = job.StartedUtc ?? DateTime.UtcNow;
                string manifestKey = RepositoryKeys.ManifestKey(policy.Id, job.Id);
                ManifestHeader manifestHeader = new ManifestHeader
                {
                    JobId = job.Id,
                    PolicyId = policy.Id,
                    BackupType = backupType,
                    BaseJobId = job.BaseJobId,
                    PointInTimeUtc = pointInTime,
                };
                ManifestStore.Writer manifestWriter = new ManifestStore.Writer(repository, manifestKey, dataKey, manifestHeader);

                long afterRowid = 0;
                while (true)
                {
                    token.ThrowIfCancellationRequested();
                    List<JobFileEntry> donePage = await _Database.JobFiles.ReadDonePageAsync(job.Id, afterRowid, ProcessPageSize, token).ConfigureAwait(false);
                    if (donePage.Count == 0)
                        break;
                    foreach (JobFileEntry done in donePage)
                    {
                        ManifestFileEntry entry = new ManifestFileEntry();
                        entry.Path = done.Path;
                        entry.SizeBytes = done.SizeBytes;
                        entry.ModifiedUtc = done.ModifiedUtc;
                        entry.ArchiveBit = done.ArchiveBit;
                        entry.ChunkHashes = new List<string>(done.ChunkHashes);
                        await manifestWriter.AddAsync(entry, token).ConfigureAwait(false);
                        afterRowid = done.Rowid;
                    }
                }

                await manifestWriter.CompleteAsync(token).ConfigureAwait(false);

                job.FileCount = manifestWriter.FileCount;
                job.BytesTotal = manifestWriter.TotalBytes;

                // Write a small encrypted metadata sidecar so the catalog can be listed and described
                // during recovery without decoding the full manifest.
                BackupRunInfo runInfo = new BackupRunInfo();
                runInfo.JobId = job.Id;
                runInfo.PolicyId = policy.Id;
                runInfo.PolicyName = policy.Name;
                runInfo.BackupType = backupType;
                runInfo.PointInTimeUtc = pointInTime;
                runInfo.FileCount = job.FileCount;
                runInfo.TotalBytes = job.BytesTotal;
                runInfo.BytesWritten = job.BytesWritten;
                runInfo.ChunksWritten = job.ChunksWritten;
                await repository.WriteObjectAsync(RepositoryKeys.InfoKey(policy.Id, job.Id), RunInfoCodec.Encode(runInfo, dataKey), token).ConfigureAwait(false);

                job.ManifestKey = manifestKey;
                job.Status = JobStatusEnum.Completed;
                job.CompletedUtc = DateTime.UtcNow;
                await _Database.BackupJobs.UpdateAsync(job, token).ConfigureAwait(false);

                await _Database.JobFiles.DeleteByJobAsync(job.Id, CancellationToken.None).ConfigureAwait(false);
                return job;
            }
            catch (OperationCanceledException)
            {
                job.Status = JobStatusEnum.Canceled;
                job.CompletedUtc = DateTime.UtcNow;
                await _Database.BackupJobs.UpdateAsync(job, CancellationToken.None).ConfigureAwait(false);
                // The user chose to stop: discard the work list so the next run starts clean.
                await _Database.JobFiles.DeleteByJobAsync(job.Id, CancellationToken.None).ConfigureAwait(false);
                throw;
            }
            catch (Exception ex)
            {
                job.Status = JobStatusEnum.Failed;
                job.Error = ex.Message;
                job.CompletedUtc = DateTime.UtcNow;
                await _Database.BackupJobs.UpdateAsync(job, CancellationToken.None).ConfigureAwait(false);
                // Keep the work list so the next run for this policy can resume from here.
                throw;
            }
        }

        /// <summary>
        /// Find a prior run for the policy that can be resumed: one that is Running (crashed) or Failed,
        /// of the same backup type, and still has files left to process. The most recent match is chosen.
        /// </summary>
        private async Task<BackupJob?> FindResumableJobAsync(string policyId, BackupTypeEnum backupType, CancellationToken token)
        {
            List<BackupJob> priorJobs = await _Database.BackupJobs.ReadByPolicyAsync(policyId, token).ConfigureAwait(false);
            BackupJob? best = null;
            foreach (BackupJob prior in priorJobs)
            {
                if (prior.Status != JobStatusEnum.Running && prior.Status != JobStatusEnum.Failed)
                    continue;
                if (prior.BackupType != backupType)
                    continue;
                if (!await _Database.JobFiles.HasPendingAsync(prior.Id, token).ConfigureAwait(false))
                    continue;

                if (best == null)
                {
                    best = prior;
                    continue;
                }

                DateTime priorKey = prior.StartedUtc ?? prior.CreatedUtc;
                DateTime bestKey = best.StartedUtc ?? best.CreatedUtc;
                if (priorKey > bestKey)
                    best = prior;
            }
            return best;
        }

        /// <summary>
        /// Scan the source and stream the work list into the database in batches, updating the coordinator's
        /// running totals as it goes and marking the scan complete (durably) at the end. Runs concurrently
        /// with the processing workers, which drain the list as it grows.
        /// </summary>
        private async Task ScanIntoWorkListAsync(
            BackupJob job,
            Policy policy,
            ExcludeMatcher matcher,
            ScanCoordinator scan,
            IProgress<BackupProgress>? progress,
            CancellationToken token)
        {
            List<JobFileEntry> batch = new List<JobFileEntry>(ScanBatchSize);
            long batchBytes = 0;

            await foreach (ScannedFile scanned in _Enumerator.ScanAsync(policy, matcher, token).ConfigureAwait(false))
            {
                JobFileEntry pending = new JobFileEntry();
                pending.Path = scanned.Path;
                pending.SizeBytes = scanned.SizeBytes;
                pending.ModifiedUtc = scanned.ModifiedUtc;
                pending.ArchiveBit = scanned.ArchiveBit;
                batch.Add(pending);
                batchBytes += scanned.SizeBytes;

                if (batch.Count >= ScanBatchSize)
                {
                    await _Database.JobFiles.AddPendingAsync(job.Id, batch, token).ConfigureAwait(false);
                    scan.Add(batch.Count, batchBytes);
                    batch.Clear();
                    batchBytes = 0;
                    progress?.Report(new BackupProgress { Scanning = true, FilesTotal = (int)scan.Files, BytesTotal = scan.Bytes, FilesDone = 0, BytesDone = 0 });
                }
            }

            if (batch.Count > 0)
            {
                await _Database.JobFiles.AddPendingAsync(job.Id, batch, token).ConfigureAwait(false);
                scan.Add(batch.Count, batchBytes);
            }

            // Record durably that the work list is complete before releasing the producer, so a crash after
            // this point resumes by processing the finished list rather than re-scanning.
            job.ScanComplete = true;
            await _Database.BackupJobs.SetScanCompleteAsync(job.Id, true, token).ConfigureAwait(false);
            scan.MarkComplete();
        }

        /// <summary>
        /// Await a task and swallow any exception. Used to observe a background task during error unwinding
        /// without masking the original failure.
        /// </summary>
        private static async Task SwallowAsync(Task task)
        {
            try
            {
                await task.ConfigureAwait(false);
            }
            catch
            {
                // Intentionally ignored: the caller is already propagating a different, primary exception.
            }
        }

        private async Task<long> ProcessPendingFilesAsync(
            BackupJob job,
            Policy policy,
            IStorageRepository repository,
            string storageTargetId,
            byte[] dataKey,
            ChunkingSettings chunking,
            Dictionary<string, ManifestFileEntry> baseline,
            BackupTypeEnum backupType,
            JobFileTotals totals,
            ScanCoordinator scan,
            int maxParallelism,
            IProgress<BackupProgress>? progress,
            CancellationToken token)
        {
            int workers = Math.Clamp(maxParallelism, 1, 32);

            ParallelBackupState state = new ParallelBackupState
            {
                Policy = policy,
                Repository = repository,
                StorageTargetId = storageTargetId,
                DataKey = dataKey,
                Chunking = chunking,
                Baseline = baseline,
                BackupType = backupType,
                Totals = totals,
                Scan = scan,
                Progress = progress,
                FilesDone = totals.DoneCount,
                BytesDone = totals.DoneBytes,
            };

            Channel<JobFileEntry> channel = Channel.CreateBounded<JobFileEntry>(
                new BoundedChannelOptions(Math.Max(16, workers * 4))
                {
                    SingleReader = false,
                    SingleWriter = true,
                    FullMode = BoundedChannelFullMode.Wait,
                });

            using (CancellationTokenSource failure = CancellationTokenSource.CreateLinkedTokenSource(token))
            {
                // Producer: stream the pending work list to the workers, paging by rowid so the same file is
                // never handed out twice even as workers mark earlier files done. Because the scanner may still
                // be appending rows, an empty page does not mean "done": only when the page is empty *and* the
                // scan has finished is the work list truly exhausted. Otherwise the producer waits briefly for
                // the scanner to add more. New rows always get a higher rowid than the cursor, so nothing is
                // missed or handed out twice.
                Task producer = Task.Run(async () =>
                {
                    try
                    {
                        long afterRowid = 0;
                        while (true)
                        {
                            List<JobFileEntry> page = await _Database.JobFiles.ReadPendingPageAsync(job.Id, afterRowid, ProcessPageSize, failure.Token).ConfigureAwait(false);
                            if (page.Count == 0)
                            {
                                if (scan.Complete)
                                    break;
                                await Task.Delay(ScanPollMilliseconds, failure.Token).ConfigureAwait(false);
                                continue;
                            }
                            foreach (JobFileEntry pending in page)
                            {
                                afterRowid = pending.Rowid;
                                await channel.Writer.WriteAsync(pending, failure.Token).ConfigureAwait(false);
                            }
                        }
                    }
                    finally
                    {
                        channel.Writer.TryComplete();
                    }
                }, failure.Token);

                // Periodically flush the live counters to the job row so another process (the TUI's
                // in-progress view) can watch a run that this process is driving silently — the per-file
                // progress observer is in-process only. Best-effort: a persistence hiccup never fails the run,
                // and the authoritative totals are written when the run completes.
                Task persister = Task.Run(async () =>
                {
                    try
                    {
                        while (true)
                        {
                            await Task.Delay(ProgressPersistMilliseconds, failure.Token).ConfigureAwait(false);
                            await _Database.BackupJobs.UpdateProgressAsync(
                                job.Id,
                                !scan.Complete,
                                Interlocked.Read(ref state.FilesDone),
                                scan.Files,
                                Interlocked.Read(ref state.BytesDone),
                                scan.Bytes,
                                failure.Token).ConfigureAwait(false);
                        }
                    }
                    catch (OperationCanceledException)
                    {
                        // The run finished or was canceled; stop persisting.
                    }
                    catch (Exception)
                    {
                        // Progress persistence is a courtesy; never let it fault the run.
                    }
                }, failure.Token);

                Task[] workerTasks = new Task[workers];
                for (int i = 0; i < workers; i++)
                {
                    workerTasks[i] = Task.Run(async () =>
                    {
                        await foreach (JobFileEntry pending in channel.Reader.ReadAllAsync(failure.Token).ConfigureAwait(false))
                        {
                            try
                            {
                                await ProcessOneFileAsync(pending, state, failure.Token).ConfigureAwait(false);
                            }
                            catch (OperationCanceledException)
                            {
                                throw;
                            }
                            catch
                            {
                                // A target-side or database failure (as opposed to a per-file source read
                                // failure, which ProcessOneFileAsync handles) aborts the whole run: cancel the
                                // siblings and let the exception surface.
                                failure.Cancel();
                                throw;
                            }
                        }
                    }, failure.Token);
                }

                try
                {
                    await Task.WhenAll(workerTasks).ConfigureAwait(false);
                }
                catch
                {
                    // Structured outcome handling below decides cancel-versus-fail.
                }
                finally
                {
                    failure.Cancel();
                    channel.Writer.TryComplete();
                }

                try
                {
                    await producer.ConfigureAwait(false);
                }
                catch
                {
                    // Producer faults are surfaced through IsFaulted below.
                }

                try
                {
                    await persister.ConfigureAwait(false);
                }
                catch
                {
                    // The persister swallows its own errors; this observes the task so it is never unobserved.
                }

                // If the user canceled, that outranks any incidental cancellation the workers observed.
                token.ThrowIfCancellationRequested();

                if (producer.IsFaulted)
                {
                    Exception inner = producer.Exception!.InnerExceptions[0];
                    if (!(inner is OperationCanceledException))
                        throw inner;
                }
                foreach (Task worker in workerTasks)
                {
                    if (!worker.IsFaulted)
                        continue;
                    Exception inner = worker.Exception!.InnerExceptions[0];
                    if (inner is OperationCanceledException)
                        continue;
                    throw inner;
                }
            }

            job.ChunksWritten += Interlocked.Read(ref state.ChunksWritten);
            job.ChunksReused += Interlocked.Read(ref state.ChunksReused);
            job.BytesWritten += Interlocked.Read(ref state.BytesWritten);
            job.BytesDeduplicated += Interlocked.Read(ref state.BytesDeduplicated);
            job.SkippedFiles += Interlocked.Read(ref state.Skipped);
            job.SkippedBytes += Interlocked.Read(ref state.SkippedBytes);
            job.CopiedFiles += Interlocked.Read(ref state.CopiedFiles);

            // Emit a final, unthrottled progress report so an observer always sees the completed totals even
            // if the last per-file report fell inside the throttle window. By now the scan has finished, so the
            // coordinator holds the final totals (for a resume these were seeded from the stored totals).
            progress?.Report(new BackupProgress
            {
                Scanning = false,
                FilesTotal = (int)scan.Files,
                FilesDone = (int)Interlocked.Read(ref state.FilesDone),
                BytesTotal = scan.Bytes,
                BytesDone = Interlocked.Read(ref state.BytesDone),
                CurrentPath = String.Empty,
            });

            return Interlocked.Read(ref state.Skipped);
        }

        private async Task ProcessOneFileAsync(JobFileEntry pending, ParallelBackupState state, CancellationToken token)
        {
            FileInfo info = new FileInfo(pending.Path);
            if (!info.Exists)
            {
                // Vanished between scan and copy: drop it so it never enters the manifest.
                await _Database.JobFiles.RemoveAsync(pending.Rowid, token).ConfigureAwait(false);
                return;
            }

            ManifestFileEntry entry = new ManifestFileEntry();
            entry.Path = pending.Path;
            entry.SizeBytes = info.Length;
            entry.ModifiedUtc = info.LastWriteTimeUtc;
            entry.ArchiveBit = _ChangeDetector.IsArchiveBitSet(pending.Path);

            state.Baseline.TryGetValue(pending.Path, out ManifestFileEntry? baselineEntry);
            bool reuse = state.BackupType != BackupTypeEnum.Full
                && baselineEntry != null
                && !_ChangeDetector.HasChanged(info, baselineEntry, state.Policy.UseArchiveBit);

            List<ChunkIndexEntry> references = new List<ChunkIndexEntry>();
            if (reuse && baselineEntry != null)
            {
                foreach (string hash in baselineEntry.ChunkHashes)
                {
                    entry.ChunkHashes.Add(hash);
                    references.Add(new ChunkIndexEntry { StorageTargetId = state.StorageTargetId, Hash = hash, CreatedUtc = DateTime.UtcNow });
                }
                Interlocked.Add(ref state.ChunksReused, baselineEntry.ChunkHashes.Count);
                Interlocked.Add(ref state.BytesDeduplicated, info.Length);
            }
            else
            {
                FileFrameResult framed;
                try
                {
                    framed = await FrameFileAsync(pending.Path, state, entry, references, token).ConfigureAwait(false);
                }
                catch (SourceUnreadableException unreadable)
                {
                    // One unreadable file (locked, permission-denied, a broken reparse point) must not sink
                    // the whole backup — log it, drop it from the work list, and move on. Target failures are
                    // a different exception and still abort.
                    Diagnostics.ArmorLog.Warn("Skipping unreadable file '" + pending.Path + "': " + (unreadable.InnerException != null ? unreadable.InnerException.Message : unreadable.Message));
                    Interlocked.Increment(ref state.Skipped);
                    Interlocked.Add(ref state.SkippedBytes, pending.SizeBytes);
                    await _Database.JobFiles.RemoveAsync(pending.Rowid, token).ConfigureAwait(false);
                    return;
                }
                Interlocked.Add(ref state.ChunksWritten, framed.ChunksWritten);
                Interlocked.Add(ref state.ChunksReused, framed.ChunksReused);
                Interlocked.Add(ref state.BytesWritten, framed.BytesWritten);
                Interlocked.Add(ref state.BytesDeduplicated, framed.BytesDeduplicated);
                // A file that was actually read and copied this run (chunked and written), as opposed to one
                // whose chunks were reused wholesale from the baseline of an incremental/differential run.
                Interlocked.Increment(ref state.CopiedFiles);
                if (state.Policy.UseArchiveBit)
                    _ChangeDetector.ClearArchiveBit(pending.Path);
            }

            // Commit the file: its chunk-index references and its done-mark. The reference upserts are
            // additive, so a crash between the two commits can only over-count a chunk (it lives a little
            // longer than needed), never under-count it (which could drop live data). Blobs are always
            // written before their references, so nothing referenced is ever missing.
            if (references.Count > 0)
                await _Database.ChunkIndex.ReferenceBatchAsync(references, token).ConfigureAwait(false);
            string chunkJson = ArmorJson.Serialize(entry.ChunkHashes);
            await _Database.JobFiles.MarkDoneAsync(pending.Rowid, entry.SizeBytes, entry.ModifiedUtc, entry.ArchiveBit, chunkJson, token).ConfigureAwait(false);

            long doneNow = Interlocked.Increment(ref state.FilesDone);
            long bytesNow = Interlocked.Add(ref state.BytesDone, info.Length);
            ReportProgress(state, doneNow, bytesNow, entry.Path);
        }

        private async Task<FileFrameResult> FrameFileAsync(string path, ParallelBackupState state, ManifestFileEntry entry, List<ChunkIndexEntry> references, CancellationToken token)
        {
            FileStream stream;
            try
            {
                // Share ReadWrite|Delete so a file another process still has open (a log, a database, an
                // app writing its cache) can still be read. A genuine open failure — permission, a broken
                // reparse point, a special file — is surfaced as SourceUnreadableException so the caller
                // skips just this file instead of aborting the whole backup.
                stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
            }
            catch (IOException ex)
            {
                throw new SourceUnreadableException(path, ex);
            }
            catch (UnauthorizedAccessException ex)
            {
                throw new SourceUnreadableException(path, ex);
            }

            FileFrameResult result = default;
            FastCdc chunker = new FastCdc(state.Chunking);
            using (stream)
            {
                IAsyncEnumerator<byte[]> chunks = chunker.ChunkAsync(stream, token).GetAsyncEnumerator(token);
                try
                {
                    while (true)
                    {
                        byte[] chunk;
                        try
                        {
                            if (!await chunks.MoveNextAsync().ConfigureAwait(false))
                                break;
                            chunk = chunks.Current;
                        }
                        catch (IOException ex)
                        {
                            throw new SourceUnreadableException(path, ex);
                        }
                        catch (UnauthorizedAccessException ex)
                        {
                            throw new SourceUnreadableException(path, ex);
                        }

                        string hash = Hasher.Sha256Hex(chunk);
                        entry.ChunkHashes.Add(hash);

                        ChunkWriteResult written;
                        if (state.Present.ContainsKey(hash))
                        {
                            // Already durably written earlier this run — reference it without touching disk.
                            written = new ChunkWriteResult(false, 0, chunk.Length);
                        }
                        else
                        {
                            // Coordinate so a given new blob is written exactly once even if several workers
                            // hold the same chunk right now. The winner writes; everyone else awaits its task,
                            // guaranteeing the blob is durable before any file references it.
                            byte[] chunkForWrite = chunk;
                            Lazy<Task<ChunkWriteResult>> lazy = state.Inflight.GetOrAdd(
                                hash,
                                h => new Lazy<Task<ChunkWriteResult>>(() => WriteChunkOnceAsync(h, chunkForWrite, state, token)));
                            written = await lazy.Value.ConfigureAwait(false);
                            state.Inflight.TryRemove(hash, out _);
                        }

                        references.Add(new ChunkIndexEntry
                        {
                            StorageTargetId = state.StorageTargetId,
                            Hash = hash,
                            StoredSizeBytes = written.StoredSize,
                            PlaintextSizeBytes = chunk.Length,
                            CreatedUtc = DateTime.UtcNow,
                        });

                        if (written.NewlyWritten)
                        {
                            result.ChunksWritten += 1;
                            result.BytesWritten += written.StoredSize;
                        }
                        else
                        {
                            result.ChunksReused += 1;
                            result.BytesDeduplicated += chunk.Length;
                        }
                    }
                }
                finally
                {
                    await chunks.DisposeAsync().ConfigureAwait(false);
                }
            }
            return result;
        }

        private static async Task<ChunkWriteResult> WriteChunkOnceAsync(string hash, byte[] chunk, ParallelBackupState state, CancellationToken token)
        {
            if (await state.Repository.ChunkExistsAsync(hash, token).ConfigureAwait(false))
            {
                state.Present[hash] = 1;
                return new ChunkWriteResult(false, 0, chunk.Length);
            }

            byte[] stored = ChunkFramer.Frame(chunk, state.DataKey, hash);
            await state.Repository.WriteChunkAsync(hash, stored, token).ConfigureAwait(false);
            state.Present[hash] = 1;
            return new ChunkWriteResult(true, stored.Length, chunk.Length);
        }

        private static void ReportProgress(ParallelBackupState state, long filesDone, long bytesDone, string currentPath)
        {
            IProgress<BackupProgress>? progress = state.Progress;
            if (progress == null)
                return;

            // Throttle to roughly ten updates a second, and let only one thread win each window so parallel
            // workers do not flood the observer with duplicate reports.
            long now = Environment.TickCount64;
            long last = Interlocked.Read(ref state.LastReportTick);
            if (now - last < 100)
                return;
            if (Interlocked.CompareExchange(ref state.LastReportTick, now, last) != last)
                return;

            // The total grows while the scan is still running, so read it from the coordinator and flag the
            // run as still scanning until the scan finishes; the bar then shows progress against a moving
            // target that settles once scanning completes.
            progress.Report(new BackupProgress
            {
                Scanning = !state.Scan.Complete,
                FilesTotal = (int)state.Scan.Files,
                FilesDone = (int)filesDone,
                BytesTotal = state.Scan.Bytes,
                BytesDone = bytesDone,
                CurrentPath = currentPath,
            });
        }

        /// <summary>
        /// Carries the live state of the source scan to the processing side of an overlapped run: the running
        /// count of scanned files and bytes, and whether the scan has finished. The work-list producer reads
        /// <see cref="Complete"/> to tell a temporarily drained list from a truly exhausted one, and the
        /// progress reporter reads the totals so the bar tracks a target that grows until the scan settles. All
        /// members are thread-safe.
        /// </summary>
        private sealed class ScanCoordinator
        {
            private long _Files;
            private long _Bytes;
            private volatile bool _Complete;

            /// <summary>Files scanned so far.</summary>
            public long Files
            {
                get { return Interlocked.Read(ref _Files); }
            }

            /// <summary>Bytes scanned so far.</summary>
            public long Bytes
            {
                get { return Interlocked.Read(ref _Bytes); }
            }

            /// <summary>Whether the scan has finished (or there was nothing to scan).</summary>
            public bool Complete
            {
                get { return _Complete; }
            }

            /// <summary>Add a scanned batch to the running totals.</summary>
            public void Add(long files, long bytes)
            {
                Interlocked.Add(ref _Files, files);
                Interlocked.Add(ref _Bytes, bytes);
            }

            /// <summary>Seed the totals to fixed values (used when resuming a run whose scan already finished).</summary>
            public void SetTotals(long files, long bytes)
            {
                Interlocked.Exchange(ref _Files, files);
                Interlocked.Exchange(ref _Bytes, bytes);
            }

            /// <summary>Signal that no more rows will be added to the work list.</summary>
            public void MarkComplete()
            {
                _Complete = true;
            }
        }

        /// <summary>Outcome of considering one chunk for storage: whether its blob was newly written and its sizes.</summary>
        private readonly struct ChunkWriteResult
        {
            public ChunkWriteResult(bool newlyWritten, long storedSize, int plaintextSize)
            {
                NewlyWritten = newlyWritten;
                StoredSize = storedSize;
                PlaintextSize = plaintextSize;
            }

            public bool NewlyWritten { get; }

            public long StoredSize { get; }

            public int PlaintextSize { get; }
        }

        /// <summary>Per-file tallies accumulated while framing, folded into the run totals with Interlocked.</summary>
        private struct FileFrameResult
        {
            public long ChunksWritten;
            public long ChunksReused;
            public long BytesWritten;
            public long BytesDeduplicated;
        }

        /// <summary>
        /// Shared, thread-safe state for the parallel copy phase. The counter fields are updated with
        /// <see cref="Interlocked"/> from several workers; the dictionaries coordinate chunk deduplication
        /// across those workers.
        /// </summary>
        private sealed class ParallelBackupState
        {
            public Policy Policy = null!;
            public IStorageRepository Repository = null!;
            public string StorageTargetId = null!;
            public byte[] DataKey = null!;
            public ChunkingSettings Chunking = null!;
            public Dictionary<string, ManifestFileEntry> Baseline = null!;
            public BackupTypeEnum BackupType;
            public JobFileTotals Totals = null!;
            public ScanCoordinator Scan = null!;
            public IProgress<BackupProgress>? Progress;

            public readonly ConcurrentDictionary<string, byte> Present = new ConcurrentDictionary<string, byte>(StringComparer.Ordinal);
            public readonly ConcurrentDictionary<string, Lazy<Task<ChunkWriteResult>>> Inflight = new ConcurrentDictionary<string, Lazy<Task<ChunkWriteResult>>>(StringComparer.Ordinal);

            public long ChunksWritten;
            public long ChunksReused;
            public long BytesWritten;
            public long BytesDeduplicated;
            public long FilesDone;
            public long BytesDone;
            public long Skipped;
            public long SkippedBytes;
            public long CopiedFiles;
            public long LastReportTick;
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

            // Stream the baseline manifest one segment at a time rather than decoding it whole, so an
            // incremental against a very large prior backup does not rebuild the entire file list — or a
            // single oversized buffer — in memory just to key it by path.
            await foreach (ManifestFileEntry entry in ManifestStore.StreamAsync(repository, baselineJob.ManifestKey!, baselineJob.Id, dataKey, token).ConfigureAwait(false))
                map[entry.Path] = entry;
            return map;
        }

        private static async Task WriteHeaderAsync(IStorageRepository repository, EncryptionKey encryptionKey, ChunkingSettings chunking, CancellationToken token)
        {
            RepositoryHeader header = RepositoryHeader.FromEncryptionKey(encryptionKey, chunking);
            byte[] bytes = Encoding.UTF8.GetBytes(Serialization.ArmorJson.Serialize(header));
            await repository.WriteObjectAsync(RepositoryKeys.HeaderKey, bytes, token).ConfigureAwait(false);
        }

    }
}
