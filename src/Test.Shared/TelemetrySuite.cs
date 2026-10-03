namespace Test.Shared
{
    using System;
    using System.Collections.Generic;
    using System.Diagnostics;
    using System.IO;
    using System.Linq;
    using System.Net;
    using System.Net.Http;
    using System.Net.Sockets;
    using System.Threading;
    using System.Threading.Tasks;
    using Armor.Core.Configuration;
    using Armor.Core.Diagnostics;
    using Armor.Core.Engine;
    using Armor.Core.Enums;
    using Armor.Core.Exceptions;
    using Armor.Core.Models;
    using Armor.Core.Scheduling;
    using Armor.Core.Security;
    using Armor.Core.Service;
    using Armor.Core.Storage;
    using Armor.Core.Telemetry;
    using Armor.Telemetry;
    using Touchstone.Core;

    /// <summary>
    /// Proves Armor emits its telemetry: every inventory category (backup pipeline and stages, per-file
    /// work, restore, verify, retention, scheduler, storage integration, key operations, process and
    /// configuration gauges, the exporter host) and the failure paths (storage failure, cancellation,
    /// unreadable source, missing chunk, refused requests, wrong password). Listens through the base class
    /// library only, so no collector is needed.
    /// </summary>
    public static class TelemetrySuite
    {
        /// <summary>
        /// Build the telemetry test suite.
        /// </summary>
        /// <returns>The telemetry suite descriptor.</returns>
        public static TestSuiteDescriptor Build()
        {
            return new TestSuiteDescriptor(
                suiteId: "Telemetry",
                displayName: "Telemetry (metrics and traces)",
                cases: new List<TestCaseDescriptor>
                {
                    Case("NoListenerPathIsSafe", "Instrumented code runs and helpers are inert with no listener of their own", async ct =>
                    {
                        using (TempWorkspace ws = new TempWorkspace())
                        using (EngineFixture fx = await EngineFixture.BuildAsync(ws, ct).ConfigureAwait(false))
                        {
                            string source = Path.Combine(ws.RootDirectory, "source");
                            WriteFile(source, "a.bin", Content(1, 9000));
                            BackupJob job = await new BackupEngine(fx.Database).RunAsync(NewPolicy(source), fx.Repository, fx.StorageTargetId, fx.EncryptionKey, fx.DataKey, fx.Chunking, BackupTypeEnum.Full, ct).ConfigureAwait(false);
                            Check.Equal(JobStatusEnum.Completed, job.Status, "backup completes with instrumentation in place");

                            ArmorTelemetry.NoteBackupSucceeded(DateTime.UtcNow);
                            ArmorTelemetry.ObserveSettings(null);
                            Check.Equal("disk", ArmorTelemetry.StorageTypeLabel(StorageTargetTypeEnum.Disk), "storage label");
                            Check.Equal("amazon_s3", ArmorTelemetry.StorageTypeLabel(StorageTargetTypeEnum.AmazonS3), "storage label");
                            Check.Equal("unknown", ArmorTelemetry.ErrorType(null), "null error type");
                            Check.True(fx.Repository is InstrumentedStorageRepository, "factory repositories are instrumented");
                        }
                    }),

                    Case("BackupEmitsPipelineTelemetry", "A backup emits job, stage, file, chunk, storage metrics and a stage span tree", async ct =>
                    {
                        using (TelemetryCapture capture = new TelemetryCapture())
                        using (TempWorkspace ws = new TempWorkspace())
                        using (EngineFixture fx = await EngineFixture.BuildAsync(ws, ct).ConfigureAwait(false))
                        {
                            string source = Path.Combine(ws.RootDirectory, "source");
                            WriteFile(source, "a.bin", Content(1, 20000));
                            WriteFile(source, "b.bin", Content(2, 15000));
                            WriteFile(source, "dup.bin", Content(1, 20000));

                            BackupJob job = await new BackupEngine(fx.Database).RunAsync(NewPolicy(source), fx.Repository, fx.StorageTargetId, fx.EncryptionKey, fx.DataKey, fx.Chunking, BackupTypeEnum.Full, ct, null, 2).ConfigureAwait(false);
                            Check.Equal(JobStatusEnum.Completed, job.Status, "backup completed");

                            Check.True(capture.Sum(TelemetryNames.BackupJobs, TelemetryNames.AttrOutcome, "success", TelemetryNames.AttrBackupType, "full") >= 1, "job counted as success");
                            Check.True(capture.Count(TelemetryNames.BackupDuration, TelemetryNames.AttrOutcome, "success") >= 1, "job duration recorded");
                            foreach (string stage in new[] { "open", "header", "baseline", "scan", "process", "manifest", "finalize" })
                            {
                                Check.True(capture.Sum(TelemetryNames.BackupStage, TelemetryNames.AttrStage, stage, TelemetryNames.AttrOutcome, "success") >= 1, "stage counted: " + stage);
                                Check.True(capture.Count(TelemetryNames.BackupStageDuration, TelemetryNames.AttrStage, stage) >= 1, "stage duration: " + stage);
                            }
                            foreach (string fileStage in new[] { "queued", "read", "hash", "frame", "dedupe_check", "upload", "commit" })
                                Check.True(capture.Count(TelemetryNames.BackupFileStageDuration, TelemetryNames.AttrStage, fileStage) >= 1, "file stage duration: " + fileStage);

                            Check.True(capture.Sum(TelemetryNames.BackupFiles, TelemetryNames.AttrFileOutcome, "copied") >= 3, "files copied");
                            Check.True(capture.Sum(TelemetryNames.BackupFilesScanned) >= 3, "files scanned");
                            Check.True(capture.Sum(TelemetryNames.BackupBytes, TelemetryNames.AttrBytesKind, "scanned") >= 55000, "bytes scanned");
                            Check.True(capture.Sum(TelemetryNames.BackupBytes, TelemetryNames.AttrBytesKind, "processed") >= 55000, "bytes processed");
                            Check.True(capture.Sum(TelemetryNames.BackupBytes, TelemetryNames.AttrBytesKind, "stored") > 0, "bytes stored");
                            Check.True(capture.Sum(TelemetryNames.BackupChunks, TelemetryNames.AttrChunkOutcome, "written") >= 1, "chunks written");
                            Check.True(capture.Sum(TelemetryNames.BackupChunks, TelemetryNames.AttrChunkOutcome, "deduplicated_run") + capture.Sum(TelemetryNames.BackupChunks, TelemetryNames.AttrChunkOutcome, "deduplicated_target") >= 1, "duplicate file content deduplicated");

                            Check.True(capture.Measurements(TelemetryNames.BackupWorkersCapacity).Any(m => m.Value == 2), "worker capacity provisioned");
                            Check.True(capture.Measurements(TelemetryNames.BackupWorkersCapacity).Any(m => m.Value == -2), "worker capacity released");
                            Check.True(capture.Measurements(TelemetryNames.BackupWorkersActive).Any(m => m.Value == 1), "worker activity tracked");
                            Check.True(capture.Measurements(TelemetryNames.BackupQueueDepth).Any(m => m.Value == 1), "queue depth tracked");
                            Check.True(capture.Measurements(TelemetryNames.BackupJobsActive).Any(m => m.Value == 1), "active jobs tracked");

                            Check.True(capture.Sum(TelemetryNames.StorageOperations, TelemetryNames.AttrStorageType, "disk", TelemetryNames.AttrStorageOperation, "write_chunk", TelemetryNames.AttrOutcome, "success") >= 1, "chunk writes counted");
                            Check.True(capture.Sum(TelemetryNames.StorageOperations, TelemetryNames.AttrStorageOperation, "write_object", TelemetryNames.AttrOutcome, "success") >= 1, "object writes counted");
                            Check.True(capture.Count(TelemetryNames.StorageOperationDuration, TelemetryNames.AttrStorageOperation, "chunk_exists") >= 1, "dedupe checks timed");
                            Check.True(capture.Sum(TelemetryNames.StorageBytes, TelemetryNames.AttrStorageDirection, "sent") > 0, "bytes sent counted");

                            Activity? jobSpan = capture.Spans(TelemetryNames.SpanBackupJob).FirstOrDefault();
                            Check.NotNull(jobSpan, "backup.job span emitted");
                            Check.Equal(ActivityStatusCode.Ok, jobSpan!.Status, "job span marked ok");
                            Check.Equal(job.Id, jobSpan.GetTagItem(TelemetryNames.AttrJobId) as string, "job span carries the job id");
                            Check.Equal(capture.Root!.SpanId, jobSpan.ParentSpanId, "job span nests under the caller's span");
                            foreach (string stage in new[] { "open", "header", "baseline", "scan", "process", "manifest", "finalize" })
                            {
                                Activity? stageSpan = capture.Spans("stage:" + stage).FirstOrDefault();
                                Check.NotNull(stageSpan, "stage span: " + stage);
                                Check.Equal(jobSpan.SpanId, stageSpan!.ParentSpanId, "stage span is a child of the job span: " + stage);
                            }
                            Activity? clientSpan = capture.Spans("disk write_object").FirstOrDefault();
                            Check.NotNull(clientSpan, "storage client span emitted");
                            Check.Equal(ActivityKind.Client, clientSpan!.Kind, "storage span is a client span");
                            Check.Equal("disk", clientSpan.GetTagItem(TelemetryNames.AttrStorageType) as string, "client span carries the storage type");

                            capture.RecordObservables();
                            Check.True(capture.Measurements(TelemetryNames.BackupLastSuccess).Any(m => m.Value > 1000000000), "last-success timestamp gauge reports");
                        }
                    }),

                    Case("IncrementalReusesBaseline", "An unchanged incremental reports unchanged files and baseline-deduplicated chunks", async ct =>
                    {
                        using (TelemetryCapture capture = new TelemetryCapture())
                        using (TempWorkspace ws = new TempWorkspace())
                        using (EngineFixture fx = await EngineFixture.BuildAsync(ws, ct).ConfigureAwait(false))
                        {
                            string source = Path.Combine(ws.RootDirectory, "source");
                            WriteFile(source, "a.bin", Content(3, 12000));
                            Policy policy = NewPolicy(source);
                            BackupEngine engine = new BackupEngine(fx.Database);
                            await engine.RunAsync(policy, fx.Repository, fx.StorageTargetId, fx.EncryptionKey, fx.DataKey, fx.Chunking, BackupTypeEnum.Full, ct).ConfigureAwait(false);
                            await engine.RunAsync(policy, fx.Repository, fx.StorageTargetId, fx.EncryptionKey, fx.DataKey, fx.Chunking, BackupTypeEnum.Incremental, ct).ConfigureAwait(false);

                            Check.True(capture.Sum(TelemetryNames.BackupJobs, TelemetryNames.AttrBackupType, "incremental", TelemetryNames.AttrOutcome, "success") >= 1, "incremental counted");
                            Check.True(capture.Sum(TelemetryNames.BackupFiles, TelemetryNames.AttrFileOutcome, "unchanged") >= 1, "unchanged file counted");
                            Check.True(capture.Sum(TelemetryNames.BackupChunks, TelemetryNames.AttrChunkOutcome, "deduplicated_baseline") >= 1, "baseline chunks reused");
                            Check.True(capture.Sum(TelemetryNames.BackupBytes, TelemetryNames.AttrBytesKind, "deduplicated") >= 12000, "deduplicated bytes counted");
                        }
                    }),

                    Case("BackupFailureIsRecorded", "A storage failure records a failed job, a failed stage, an error, and an error span", async ct =>
                    {
                        using (TelemetryCapture capture = new TelemetryCapture())
                        using (TempWorkspace ws = new TempWorkspace())
                        using (EngineFixture fx = await EngineFixture.BuildAsync(ws, ct).ConfigureAwait(false))
                        {
                            string source = Path.Combine(ws.RootDirectory, "source");
                            WriteFile(source, "a.bin", Content(4, 20000));
                            FailingRepository failing = new FailingRepository(fx.Repository, 1);
                            bool threw = false;
                            try
                            {
                                await new BackupEngine(fx.Database).RunAsync(NewPolicy(source), failing, fx.StorageTargetId, fx.EncryptionKey, fx.DataKey, fx.Chunking, BackupTypeEnum.Full, ct).ConfigureAwait(false);
                            }
                            catch (IOException)
                            {
                                threw = true;
                            }
                            Check.True(threw, "backup failed");

                            Check.True(capture.Sum(TelemetryNames.BackupJobs, TelemetryNames.AttrOutcome, "failure", TelemetryNames.AttrErrorType, "IOException") >= 1, "failed job counted with error type");
                            Check.True(capture.Sum(TelemetryNames.BackupStage, TelemetryNames.AttrStage, "process", TelemetryNames.AttrOutcome, "failure") >= 1, "process stage counted as failed");
                            Check.True(capture.Sum(TelemetryNames.Errors, TelemetryNames.AttrComponent, "backup", TelemetryNames.AttrErrorType, "IOException") >= 1, "error counted by component and type");

                            Activity? jobSpan = capture.Spans(TelemetryNames.SpanBackupJob).FirstOrDefault();
                            Check.NotNull(jobSpan, "job span emitted");
                            Check.Equal(ActivityStatusCode.Error, jobSpan!.Status, "job span marked error");
                            Check.Equal("IOException", jobSpan.GetTagItem(TelemetryNames.AttrErrorType) as string, "job span carries error.type");
                            Check.True(jobSpan.Events.Any(e => e.Name == "exception"), "exception recorded on the span");
                            Activity? processSpan = capture.Spans("stage:process").FirstOrDefault();
                            Check.Equal(ActivityStatusCode.Error, processSpan!.Status, "failing stage span marked error");
                        }
                    }),

                    Case("CanceledBackupIsNotAnError", "A canceled backup is counted as canceled without an error status", async ct =>
                    {
                        using (TelemetryCapture capture = new TelemetryCapture())
                        using (TempWorkspace ws = new TempWorkspace())
                        using (EngineFixture fx = await EngineFixture.BuildAsync(ws, ct).ConfigureAwait(false))
                        using (CancellationTokenSource cts = new CancellationTokenSource())
                        {
                            string source = Path.Combine(ws.RootDirectory, "source");
                            WriteFile(source, "a.bin", Content(5, 9000));
                            cts.Cancel();
                            bool canceled = false;
                            try
                            {
                                await new BackupEngine(fx.Database).RunAsync(NewPolicy(source), fx.Repository, fx.StorageTargetId, fx.EncryptionKey, fx.DataKey, fx.Chunking, BackupTypeEnum.Full, cts.Token).ConfigureAwait(false);
                            }
                            catch (OperationCanceledException)
                            {
                                canceled = true;
                            }
                            Check.True(canceled, "backup canceled");
                            Check.True(capture.Sum(TelemetryNames.BackupJobs, TelemetryNames.AttrOutcome, "canceled") >= 1, "canceled job counted");
                            Activity? jobSpan = capture.Spans(TelemetryNames.SpanBackupJob).FirstOrDefault();
                            Check.NotNull(jobSpan, "job span emitted");
                            Check.True(jobSpan!.Status != ActivityStatusCode.Error, "cancellation is not an error status");
                            Check.Equal("canceled", jobSpan.GetTagItem(TelemetryNames.AttrOutcome) as string, "span outcome is canceled");
                        }
                    }),

                    Case("UnreadableFileIsCounted", "An unreadable source file is counted as skipped and as a source error", async ct =>
                    {
                        if (OperatingSystem.IsWindows())
                            return; // Removing read permission portably needs POSIX file modes.

                        using (TelemetryCapture capture = new TelemetryCapture())
                        using (TempWorkspace ws = new TempWorkspace())
                        using (EngineFixture fx = await EngineFixture.BuildAsync(ws, ct).ConfigureAwait(false))
                        {
                            string source = Path.Combine(ws.RootDirectory, "source");
                            WriteFile(source, "ok.bin", Content(6, 5000));
                            WriteFile(source, "locked.bin", Content(7, 5000));
                            string locked = Path.Combine(source, "locked.bin");
                            File.SetUnixFileMode(locked, UnixFileMode.None);
                            try
                            {
                                bool readable;
                                try
                                {
                                    using (FileStream probe = File.OpenRead(locked))
                                        readable = true;
                                }
                                catch (UnauthorizedAccessException)
                                {
                                    readable = false;
                                }
                                if (readable)
                                    return; // Running as root: permissions are not enforced.

                                BackupJob job = await new BackupEngine(fx.Database).RunAsync(NewPolicy(source), fx.Repository, fx.StorageTargetId, fx.EncryptionKey, fx.DataKey, fx.Chunking, BackupTypeEnum.Full, ct).ConfigureAwait(false);
                                Check.Equal(1L, job.SkippedFiles, "one file skipped");
                                Check.True(capture.Sum(TelemetryNames.BackupFiles, TelemetryNames.AttrFileOutcome, "skipped") >= 1, "skipped file counted");
                                Check.True(capture.Sum(TelemetryNames.BackupBytes, TelemetryNames.AttrBytesKind, "skipped") >= 5000, "skipped bytes counted");
                                Check.True(capture.Sum(TelemetryNames.Errors, TelemetryNames.AttrComponent, "source") >= 1, "source error counted");
                            }
                            finally
                            {
                                File.SetUnixFileMode(locked, UnixFileMode.UserRead | UnixFileMode.UserWrite);
                            }
                        }
                    }),

                    Case("RestoreAndVerifyEmitTelemetry", "Restore and verify emit job, stage, file-stage, chunk, and span telemetry", async ct =>
                    {
                        using (TelemetryCapture capture = new TelemetryCapture())
                        using (TempWorkspace ws = new TempWorkspace())
                        using (EngineFixture fx = await EngineFixture.BuildAsync(ws, ct).ConfigureAwait(false))
                        {
                            string source = Path.Combine(ws.RootDirectory, "source");
                            WriteFile(source, "a.bin", Content(8, 14000));
                            BackupJob job = await new BackupEngine(fx.Database).RunAsync(NewPolicy(source), fx.Repository, fx.StorageTargetId, fx.EncryptionKey, fx.DataKey, fx.Chunking, BackupTypeEnum.Full, ct).ConfigureAwait(false);

                            RestoreEngine engine = new RestoreEngine(fx.Database);
                            long verified = await engine.VerifyAsync(job, fx.Repository, fx.DataKey, ct).ConfigureAwait(false);
                            RestoreJob rj = new RestoreJob();
                            rj.BackupJobId = job.Id;
                            rj.Scope = RestoreScopeEnum.All;
                            rj.DestinationRoot = Path.Combine(ws.RootDirectory, "restore");
                            await engine.RunAsync(rj, job, fx.Repository, fx.DataKey, ct).ConfigureAwait(false);

                            Check.True(capture.Sum(TelemetryNames.RestoreJobs, TelemetryNames.AttrOutcome, "success") >= 1, "restore counted");
                            Check.True(capture.Count(TelemetryNames.RestoreDuration) >= 1, "restore duration recorded");
                            foreach (string stage in new[] { "open", "restore", "finalize" })
                                Check.True(capture.Sum(TelemetryNames.RestoreStage, TelemetryNames.AttrStage, stage, TelemetryNames.AttrOutcome, "success") >= 1, "restore stage: " + stage);
                            foreach (string fileStage in new[] { "download", "decrypt", "write" })
                                Check.True(capture.Count(TelemetryNames.RestoreFileStageDuration, TelemetryNames.AttrStage, fileStage) >= 1, "restore file stage: " + fileStage);
                            Check.True(capture.Sum(TelemetryNames.RestoreFiles) >= 1, "restored files counted");
                            Check.True(capture.Sum(TelemetryNames.RestoreBytes) >= 14000, "restored bytes counted");
                            Check.True(capture.Sum(TelemetryNames.StorageOperations, TelemetryNames.AttrStorageOperation, "read_chunk", TelemetryNames.AttrOutcome, "success") >= 1, "chunk reads counted");
                            Check.True(capture.Sum(TelemetryNames.StorageBytes, TelemetryNames.AttrStorageDirection, "received") > 0, "bytes received counted");

                            Check.True(capture.Sum(TelemetryNames.VerifyRuns, TelemetryNames.AttrOutcome, "success") >= 1, "verify counted");
                            Check.True(capture.Sum(TelemetryNames.VerifyChunks, TelemetryNames.AttrChunkOutcome, "verified") >= verified, "verified chunks counted");

                            Activity? restoreSpan = capture.Spans(TelemetryNames.SpanRestoreJob).FirstOrDefault();
                            Check.NotNull(restoreSpan, "restore.job span emitted");
                            Check.Equal(ActivityStatusCode.Ok, restoreSpan!.Status, "restore span ok");
                            Check.True(capture.Spans("stage:restore").Any(s => s.ParentSpanId == restoreSpan.SpanId), "restore stage nests under restore.job");
                            Check.NotNull(capture.Spans(TelemetryNames.SpanVerifyJob).FirstOrDefault(), "verify.job span emitted");
                        }
                    }),

                    Case("VerifyMissingChunkIsFailure", "Verification of a point with a missing chunk counts a missing chunk and a failed run", async ct =>
                    {
                        using (TelemetryCapture capture = new TelemetryCapture())
                        using (TempWorkspace ws = new TempWorkspace())
                        using (EngineFixture fx = await EngineFixture.BuildAsync(ws, ct).ConfigureAwait(false))
                        {
                            string source = Path.Combine(ws.RootDirectory, "source");
                            WriteFile(source, "a.bin", Content(9, 9000));
                            BackupJob job = await new BackupEngine(fx.Database).RunAsync(NewPolicy(source), fx.Repository, fx.StorageTargetId, fx.EncryptionKey, fx.DataKey, fx.Chunking, BackupTypeEnum.Full, ct).ConfigureAwait(false);

                            List<string> chunkKeys = new List<string>();
                            await foreach (string key in fx.Repository.EnumerateKeysAsync(RepositoryKeys.ChunksPrefix, ct).ConfigureAwait(false))
                                chunkKeys.Add(key);
                            Check.True(chunkKeys.Count > 0, "chunks were written");
                            foreach (string key in chunkKeys)
                                await fx.Repository.DeleteObjectAsync(key, ct).ConfigureAwait(false);

                            bool threw = false;
                            try
                            {
                                await new RestoreEngine(fx.Database).VerifyAsync(job, fx.Repository, fx.DataKey, ct).ConfigureAwait(false);
                            }
                            catch (ArmorStorageException)
                            {
                                threw = true;
                            }
                            Check.True(threw, "verification failed");
                            Check.True(capture.Sum(TelemetryNames.VerifyChunks, TelemetryNames.AttrChunkOutcome, "missing") >= 1, "missing chunk counted");
                            Check.True(capture.Sum(TelemetryNames.VerifyRuns, TelemetryNames.AttrOutcome, "failure", TelemetryNames.AttrErrorType, "ArmorStorageException") >= 1, "failed verify counted");
                            Check.True(capture.Sum(TelemetryNames.Errors, TelemetryNames.AttrComponent, "verify") >= 1, "verify error counted");
                            Check.True(capture.Sum(TelemetryNames.StorageOperations, TelemetryNames.AttrStorageOperation, "enumerate", TelemetryNames.AttrOutcome, "success") >= 1, "enumeration counted");
                            Check.Equal(ActivityStatusCode.Error, capture.Spans(TelemetryNames.SpanVerifyJob).First().Status, "verify span marked error");
                        }
                    }),

                    Case("RetentionEmitsTelemetry", "Retention emits run, prune and sweep stages, and pruned counts", async ct =>
                    {
                        using (TelemetryCapture capture = new TelemetryCapture())
                        using (TempWorkspace ws = new TempWorkspace())
                        using (EngineFixture fx = await EngineFixture.BuildAsync(ws, ct).ConfigureAwait(false))
                        {
                            string source = Path.Combine(ws.RootDirectory, "source");
                            WriteFile(source, "x.bin", Content(10, 12000));
                            Policy policy = NewPolicy(source);
                            policy.RetentionDays = 30;
                            BackupEngine backup = new BackupEngine(fx.Database);
                            BackupJob first = await backup.RunAsync(policy, fx.Repository, fx.StorageTargetId, fx.EncryptionKey, fx.DataKey, fx.Chunking, BackupTypeEnum.Full, ct).ConfigureAwait(false);
                            WriteFile(source, "x.bin", Content(11, 16000));
                            await backup.RunAsync(policy, fx.Repository, fx.StorageTargetId, fx.EncryptionKey, fx.DataKey, fx.Chunking, BackupTypeEnum.Full, ct).ConfigureAwait(false);

                            RetentionResult result = await new RetentionManager(fx.Database).RunAsync(policy, fx.Repository, fx.StorageTargetId, fx.DataKey, first.CompletedUtc!.Value.AddDays(40), ct).ConfigureAwait(false);
                            Check.Equal(1, result.JobsPruned, "one point pruned");
                            Check.True(capture.Sum(TelemetryNames.RetentionRuns, TelemetryNames.AttrOutcome, "success") >= 1, "retention run counted");
                            Check.True(capture.Sum(TelemetryNames.RetentionStage, TelemetryNames.AttrStage, "prune", TelemetryNames.AttrOutcome, "success") >= 1, "prune stage counted");
                            Check.True(capture.Sum(TelemetryNames.RetentionStage, TelemetryNames.AttrStage, "sweep", TelemetryNames.AttrOutcome, "success") >= 1, "sweep stage counted");
                            Check.True(capture.Sum(TelemetryNames.RetentionJobsPruned) >= 1, "pruned jobs counted");
                            Check.True(capture.Sum(TelemetryNames.RetentionChunksDeleted) >= 1, "deleted chunks counted");
                            Activity? run = capture.Spans(TelemetryNames.SpanRetentionRun).FirstOrDefault();
                            Check.NotNull(run, "retention.run span emitted");
                            Check.True(capture.Spans("stage:sweep").Any(s => s.ParentSpanId == run!.SpanId), "sweep stage nests under retention.run");
                        }
                    }),

                    Case("ServiceAndSchedulerEmitTelemetry", "A scheduled backup emits a tick span tree, decisions, and gauges; a refused request is counted", async ct =>
                    {
                        using (TelemetryCapture capture = new TelemetryCapture())
                        using (TempWorkspace ws = new TempWorkspace())
                        using (ArmorContext context = await ArmorContext.CreateAsync(new ArmorPaths(ws.Combine("home")), ct).ConfigureAwait(false))
                        {
                            context.Settings.Chunking.MinSizeBytes = 1024;
                            context.Settings.Chunking.AvgSizeBytes = 2048;
                            context.Settings.Chunking.MaxSizeBytes = 8192;
                            EncryptionKeyService keyService = new EncryptionKeyService(context.Database);
                            ProvisionedKey provisioned = await keyService.ProvisionAsync("tel-key", "tel pass", null, 50000, ct).ConfigureAwait(false);
                            StorageTargetService targetService = new StorageTargetService(context.Database, context.CredentialProtector);
                            StorageTarget target = new StorageTarget();
                            target.Name = "tel-disk";
                            target.Type = StorageTargetTypeEnum.Disk;
                            target.DiskPath = ws.Combine("repo");
                            await targetService.CreateAsync(target, ct).ConfigureAwait(false);
                            Check.True(await targetService.ValidateAsync(target.Id, ct).ConfigureAwait(false), "target validates");

                            string source = ws.Combine("source");
                            WriteFile(source, "doc.txt", Content(12, 6000));
                            Policy policy = new Policy();
                            policy.Name = "tel-policy";
                            policy.IncludePaths.Add(source);
                            policy.StorageTargetId = target.Id;
                            policy.EncryptionKeyId = provisioned.Key.Id;
                            await context.Database.Policies.CreateAsync(policy, ct).ConfigureAwait(false);

                            Schedule due = new Schedule();
                            due.PolicyId = policy.Id;
                            due.CronExpression = "*/5 * * * *";
                            due.NextRunUtc = DateTime.UtcNow.AddMinutes(-1);
                            await context.Database.Schedules.CreateAsync(due, ct).ConfigureAwait(false);

                            SchedulerService scheduler = new SchedulerService(context);
                            int ran = await scheduler.TickAsync(_ => Task.FromResult<byte[]?>(provisioned.DataKey), DateTime.UtcNow, ct).ConfigureAwait(false);
                            Check.Equal(1, ran, "the due schedule ran");

                            Check.True(capture.Sum(TelemetryNames.SchedulerTicks, TelemetryNames.AttrOutcome, "success") >= 1, "tick counted");
                            Check.True(capture.Count(TelemetryNames.SchedulerTickDuration) >= 1, "tick duration recorded");
                            Check.True(capture.Sum(TelemetryNames.SchedulerDecisions, TelemetryNames.AttrSchedulerDecision, "ran") >= 1, "ran decision counted");
                            Check.True(capture.Sum(TelemetryNames.BackupStage, TelemetryNames.AttrStage, "prepare", TelemetryNames.AttrOutcome, "success") >= 1, "prepare stage counted");
                            Check.True(capture.Sum(TelemetryNames.BackupStage, TelemetryNames.AttrStage, "retention", TelemetryNames.AttrOutcome, "success") >= 1, "retention stage counted");
                            Check.True(capture.Sum(TelemetryNames.StorageOperations, TelemetryNames.AttrStorageOperation, "validate", TelemetryNames.AttrOutcome, "success") >= 1, "validation counted");
                            Check.True(capture.Sum(TelemetryNames.KeyOperations, TelemetryNames.AttrKeyOperation, "provision", TelemetryNames.AttrOutcome, "success") >= 1, "key provision counted");

                            Activity? tick = capture.Spans(TelemetryNames.SpanSchedulerTick).FirstOrDefault();
                            Activity? run = capture.Spans(TelemetryNames.SpanBackupRun).FirstOrDefault();
                            Activity? job = capture.Spans(TelemetryNames.SpanBackupJob).FirstOrDefault();
                            Check.NotNull(tick, "scheduler.tick span emitted");
                            Check.NotNull(run, "backup.run span emitted");
                            Check.NotNull(job, "backup.job span emitted");
                            Check.Equal(tick!.SpanId, run!.ParentSpanId, "backup.run nests under scheduler.tick");
                            Check.Equal(run.SpanId, job!.ParentSpanId, "backup.job nests under backup.run");
                            Check.True(capture.Spans("stage:prepare").Any(s => s.ParentSpanId == run.SpanId), "prepare stage nests under backup.run");
                            Check.True(capture.Spans(TelemetryNames.SpanRetentionRun).Any(), "retention.run span emitted");

                            // A due schedule whose key is unavailable is left due and reported as pending.
                            Schedule waiting = new Schedule();
                            waiting.PolicyId = policy.Id;
                            waiting.CronExpression = "*/5 * * * *";
                            waiting.NextRunUtc = DateTime.UtcNow.AddMinutes(-1);
                            await context.Database.Schedules.CreateAsync(waiting, ct).ConfigureAwait(false);
                            await scheduler.TickAsync(_ => Task.FromResult<byte[]?>(null), DateTime.UtcNow, ct).ConfigureAwait(false);
                            Check.True(capture.Sum(TelemetryNames.SchedulerDecisions, TelemetryNames.AttrSchedulerDecision, "key_unavailable") >= 1, "key-unavailable decision counted");
                            capture.RecordObservables();
                            Check.True(capture.Measurements(TelemetryNames.SchedulerLastTick).Any(m => m.Value > 1000000000), "last-tick gauge reports");
                            Check.True(capture.Measurements(TelemetryNames.SchedulerSchedulesPending).Any(), "pending gauge reports");

                            // Refused requests: a held run lock and a missing policy.
                            RunLock runLock = new RunLock(context.Paths.StateDirectory);
                            using (RunLockHandle? held = runLock.TryAcquire(policy.Id))
                            {
                                Check.NotNull(held, "test holds the run lock");
                                bool rejected = false;
                                try
                                {
                                    await new BackupService(context).RunAsync(policy.Id, provisioned.DataKey, null, false, ct).ConfigureAwait(false);
                                }
                                catch (PolicyAlreadyRunningException)
                                {
                                    rejected = true;
                                }
                                Check.True(rejected, "held lock rejects the run");
                            }
                            Check.True(capture.Sum(TelemetryNames.BackupLockRejections) >= 1, "lock rejection counted");
                            Check.True(capture.Sum(TelemetryNames.Errors, TelemetryNames.AttrComponent, "backup", TelemetryNames.AttrErrorType, "PolicyAlreadyRunningException") >= 1, "rejection counted as an error");

                            bool missing = false;
                            try
                            {
                                await new BackupService(context).RunAsync("pol_missing", provisioned.DataKey, null, false, ct).ConfigureAwait(false);
                            }
                            catch (ArmorException)
                            {
                                missing = true;
                            }
                            Check.True(missing, "missing policy refused");
                            Check.True(capture.Sum(TelemetryNames.BackupStage, TelemetryNames.AttrStage, "prepare", TelemetryNames.AttrOutcome, "failure", TelemetryNames.AttrErrorType, "ArmorException") >= 1, "failed prepare stage counted");
                            Check.True(capture.Spans(TelemetryNames.SpanBackupRun).Any(s => s.Status == ActivityStatusCode.Error), "refused run span marked error");
                        }
                    }),

                    Case("StorageAndKeyFailuresAreCounted", "Storage-call and key-unlock failures are counted by error type; chunk spans follow the toggle", async ct =>
                    {
                        using (TelemetryCapture capture = new TelemetryCapture())
                        using (TempWorkspace ws = new TempWorkspace())
                        using (EngineFixture fx = await EngineFixture.BuildAsync(ws, ct).ConfigureAwait(false))
                        {
                            InstrumentedStorageRepository failing = new InstrumentedStorageRepository(new FailingRepository(fx.Repository, 1), "amazon_s3");
                            bool threw = false;
                            try
                            {
                                await failing.WriteChunkAsync(new string('a', 64), new byte[16], ct).ConfigureAwait(false);
                            }
                            catch (IOException)
                            {
                                threw = true;
                            }
                            Check.True(threw, "chunk write failed");
                            Check.True(capture.Sum(TelemetryNames.StorageOperations, TelemetryNames.AttrStorageType, "amazon_s3", TelemetryNames.AttrStorageOperation, "write_chunk", TelemetryNames.AttrOutcome, "failure", TelemetryNames.AttrErrorType, "IOException") >= 1, "storage failure counted");
                            Check.True(capture.Sum(TelemetryNames.Errors, TelemetryNames.AttrComponent, "storage", TelemetryNames.AttrErrorType, "IOException") >= 1, "storage error counted");

                            bool readFailed = false;
                            try
                            {
                                await failing.ReadObjectAsync("does/not/exist", ct).ConfigureAwait(false);
                            }
                            catch (Exception)
                            {
                                readFailed = true;
                            }
                            Check.True(readFailed, "missing object read failed");
                            Check.True(capture.Spans("amazon_s3 read_object").Any(s => s.Status == ActivityStatusCode.Error), "failed object read span marked error");

                            bool before = ArmorTelemetry.TraceChunkOperations;
                            ArmorTelemetry.TraceChunkOperations = true;
                            try
                            {
                                InstrumentedStorageRepository traced = new InstrumentedStorageRepository(fx.Repository, "azure_blob");
                                await traced.WriteChunkAsync(new string('b', 64), new byte[16], ct).ConfigureAwait(false);
                                Check.True(await traced.ChunkExistsAsync(new string('b', 64), ct).ConfigureAwait(false), "chunk exists");
                            }
                            finally
                            {
                                ArmorTelemetry.TraceChunkOperations = before;
                            }
                            Check.True(capture.Spans("azure_blob write_chunk").Any(), "chunk span emitted when tracing chunks");
                            Check.True(capture.Spans("azure_blob chunk_exists").Any(s => Equals(s.GetTagItem("armor.storage.exists"), true)), "exists span carries the result");

                            Keystore keystore = new Keystore();
                            ProvisionedKey key = keystore.Provision("bad", "right password", null, 1000);
                            bool wrong = false;
                            try
                            {
                                keystore.UnlockWithPassphrase(key.Key, "wrong password");
                            }
                            catch (ArmorCryptoException)
                            {
                                wrong = true;
                            }
                            Check.True(wrong, "wrong password rejected");
                            Check.True(capture.Sum(TelemetryNames.KeyOperations, TelemetryNames.AttrKeyOperation, "unlock_passphrase", TelemetryNames.AttrOutcome, "failure", TelemetryNames.AttrErrorType, "ArmorCryptoException") >= 1, "failed unlock counted");
                            Check.True(capture.Count(TelemetryNames.KeyOperationDuration, TelemetryNames.AttrKeyOperation, "unlock_passphrase") >= 1, "unlock duration recorded");
                            Check.True(capture.Sum(TelemetryNames.Errors, TelemetryNames.AttrComponent, "key") >= 1, "key error counted");
                            Check.True(capture.Spans(TelemetryNames.SpanKeyOperation).Any(s => s.Status == ActivityStatusCode.Error), "failed key span marked error");
                        }
                    }),

                    Case("RecoveryEmitsTelemetry", "Disaster-recovery open and browse emit operation metrics and spans", async ct =>
                    {
                        using (TelemetryCapture capture = new TelemetryCapture())
                        using (TempWorkspace ws = new TempWorkspace())
                        using (ArmorContext context = await ArmorContext.CreateAsync(new ArmorPaths(ws.Combine("home")), ct).ConfigureAwait(false))
                        {
                            context.Settings.Chunking.MinSizeBytes = 1024;
                            context.Settings.Chunking.AvgSizeBytes = 2048;
                            context.Settings.Chunking.MaxSizeBytes = 8192;
                            EncryptionKeyService keyService = new EncryptionKeyService(context.Database);
                            ProvisionedKey provisioned = await keyService.ProvisionAsync("rec-key", "rec pass", null, 50000, ct).ConfigureAwait(false);
                            StorageTargetService targetService = new StorageTargetService(context.Database, context.CredentialProtector);
                            StorageTarget target = new StorageTarget();
                            target.Name = "rec-disk";
                            target.Type = StorageTargetTypeEnum.Disk;
                            target.DiskPath = ws.Combine("repo");
                            await targetService.CreateAsync(target, ct).ConfigureAwait(false);
                            string source = ws.Combine("source");
                            WriteFile(source, "doc.txt", Content(13, 6000));
                            Policy policy = new Policy();
                            policy.Name = "rec-policy";
                            policy.IncludePaths.Add(source);
                            policy.StorageTargetId = target.Id;
                            policy.EncryptionKeyId = provisioned.Key.Id;
                            await context.Database.Policies.CreateAsync(policy, ct).ConfigureAwait(false);
                            await new BackupService(context).RunAsync(policy.Id, provisioned.DataKey, BackupTypeEnum.Full, false, ct).ConfigureAwait(false);

                            RecoveryService recovery = new RecoveryService(context);
                            RecoverySession session = await recovery.OpenAsync(target.Id, "rec pass", ct).ConfigureAwait(false);
                            List<RecoveryPoint> points = await session.BrowseAsync(ct).ConfigureAwait(false);
                            Check.Equal(1, points.Count, "one recovery point");
                            await session.ListFilesAsync(points[0], ct).ConfigureAwait(false);

                            foreach (string operation in new[] { "open", "browse", "list_files" })
                                Check.True(capture.Sum(TelemetryNames.RecoveryOperations, TelemetryNames.AttrRecoveryOperation, operation, TelemetryNames.AttrOutcome, "success") >= 1, "recovery operation counted: " + operation);
                            Check.True(capture.Spans("recovery.browse").Any(), "recovery.browse span emitted");

                            bool wrong = false;
                            try
                            {
                                await recovery.OpenAsync(target.Id, "not the password", ct).ConfigureAwait(false);
                            }
                            catch (ArmorCryptoException)
                            {
                                wrong = true;
                            }
                            Check.True(wrong, "wrong recovery password rejected");
                            Check.True(capture.Sum(TelemetryNames.RecoveryOperations, TelemetryNames.AttrRecoveryOperation, "open", TelemetryNames.AttrOutcome, "failure") >= 1, "failed recovery open counted");

                            int reconciled = await new StartupMaintenance(context).ReconcileInterruptedBackupsAsync(ct).ConfigureAwait(false);
                            Check.Equal(0, reconciled, "nothing to reconcile");
                            Check.NotNull(await new StartupMaintenance(context).PublishLastBackupSuccessAsync(ct).ConfigureAwait(false), "last success seeded from the database");
                            Check.True(capture.Spans(TelemetryNames.SpanStartupReconcile).Any(), "startup.reconcile span emitted");
                        }
                    }),

                    Sync("ProcessAndConfigGauges", "Build-info and safe configuration gauges report", () =>
                    {
                        using (TelemetryCapture capture = new TelemetryCapture())
                        {
                            ArmorSettings settings = new ArmorSettings();
                            ArmorTelemetry.ObserveSettings(settings);
                            capture.RecordObservables();
                            CapturedMeasurement? info = capture.Measurements(TelemetryNames.BuildInfo).FirstOrDefault();
                            Check.NotNull(info, "build info reported");
                            Check.Equal(1.0, info!.Value, "build info is 1");
                            Check.True(info.Tags.ContainsKey(TelemetryNames.AttrVersion) && info.Tags[TelemetryNames.AttrVersion].Length > 0, "build info carries the version");
                            Check.True(info.Tags.ContainsKey(TelemetryNames.AttrOsType), "build info carries the OS");
                            Check.True(capture.Measurements(TelemetryNames.ConfigSchedulerTickInterval).Any(m => m.Value == settings.SchedulerTickSeconds), "tick interval gauge reports");
                            Check.True(capture.Measurements(TelemetryNames.ConfigChunkSize, TelemetryNames.AttrChunkBound, "avg").Any(m => m.Value == settings.Chunking.AvgSizeBytes), "chunk size gauge reports");
                            Check.True(capture.Measurements(TelemetryNames.ConfigTraceChunkOperations).Any(), "trace-chunk toggle gauge reports");
                        }
                    }),

                    Sync("TelemetrySettingsDefaultsAndOverrides", "Telemetry settings default to off and loopback, clamp, and honor environment overrides", () =>
                    {
                        TelemetrySettings defaults = new TelemetrySettings();
                        Check.False(defaults.Enabled, "export is off by default");
                        Check.Equal("http://127.0.0.1:4317", defaults.OtlpEndpoint, "OTLP defaults to loopback gRPC");
                        Check.Equal("grpc", defaults.OtlpProtocol, "protocol defaults to grpc");
                        Check.Equal("127.0.0.1", defaults.PrometheusHostname, "Prometheus binds loopback");
                        Check.Equal(9464, defaults.AgentPrometheusPort, "agent scrape port");
                        Check.Equal(9465, defaults.TuiPrometheusPort, "TUI scrape port");
                        Check.Equal("http://127.0.0.1:3100/otlp", defaults.LokiEndpoint, "Loki defaults to loopback");
                        Check.False(defaults.TraceChunkOperations, "chunk spans off by default");

                        defaults.TraceSamplingRatio = 5;
                        Check.Equal(1.0, defaults.TraceSamplingRatio, "sampling clamps high");
                        defaults.MetricsExportIntervalMs = 1;
                        Check.Equal(1000, defaults.MetricsExportIntervalMs, "export interval clamps low");
                        defaults.OtlpProtocol = "bogus";
                        Check.Equal("grpc", defaults.OtlpProtocol, "unknown protocol falls back to grpc");
                        defaults.ServiceName = " ";
                        Check.Equal("armor", defaults.ServiceName, "blank service name restores the default");

                        Dictionary<string, string> environment = new Dictionary<string, string>
                        {
                            { "ARMOR_TELEMETRY_ENABLED", "true" },
                            { "ARMOR_TELEMETRY_OTLP_ENDPOINT", "http://127.0.0.1:4318" },
                            { "ARMOR_TELEMETRY_OTLP_PROTOCOL", "httpprotobuf" },
                            { "ARMOR_TELEMETRY_PROMETHEUS_ENABLED", "true" },
                            { "ARMOR_TELEMETRY_LOKI_ENABLED", "true" },
                            { "ARMOR_TELEMETRY_TRACE_CHUNKS", "true" },
                        };
                        SettingsManager manager = new SettingsManager(new ArmorPaths(Path.GetTempPath()), name => environment.TryGetValue(name, out string? value) ? value : null);
                        ArmorSettings settings = new ArmorSettings();
                        manager.ApplyEnvironmentOverrides(settings);
                        Check.True(settings.Telemetry.Enabled, "enabled by environment");
                        Check.Equal("http://127.0.0.1:4318", settings.Telemetry.OtlpEndpoint, "endpoint by environment");
                        Check.Equal("httpprotobuf", settings.Telemetry.OtlpProtocol, "protocol by environment");
                        Check.True(settings.Telemetry.PrometheusEnabled, "Prometheus by environment");
                        Check.True(settings.Telemetry.LokiEnabled, "Loki by environment");
                        Check.True(settings.Telemetry.TraceChunkOperations, "chunk spans by environment");

                        ArmorSettings parsed = manager.Parse("{ \"Telemetry\": { \"Enabled\": true, \"AgentPrometheusPort\": 70000 } }");
                        Check.True(parsed.Telemetry.Enabled, "telemetry section parses from armor.json");
                        Check.Equal(65535, parsed.Telemetry.AgentPrometheusPort, "port clamps");
                        Check.NotNull(manager.Parse("{ \"Telemetry\": null }").Telemetry, "null section restores defaults");
                    }),

                    Case("TelemetryHostExportsOrStaysInert", "The exporter host is inert when disabled and serves the Armor metrics when enabled", async ct =>
                    {
                        using (TelemetryHost inert = TelemetryHost.Start(new TelemetrySettings(), "test", 9464))
                            Check.False(inert.IsExporting, "disabled host exports nothing");
                        using (TelemetryHost none = TelemetryHost.Start(null, "test", 9464))
                            Check.False(none.IsExporting, "null settings export nothing");

                        TelemetrySettings settings = new TelemetrySettings();
                        settings.Enabled = true;
                        settings.OtlpEnabled = false;
                        settings.PrometheusEnabled = true;
                        int port = FreePort();
                        Radiant.RadiantSettings mapped = TelemetryHost.BuildSettings(settings, "test", port);
                        Check.Equal("armor-test", mapped.ServiceName, "service name carries the role");
                        Check.True(mapped.Sources.MeterNames.Contains(TelemetryNames.MeterName), "host subscribes to the Armor meter");
                        Check.True(mapped.Sources.ActivitySourceNames.Contains(TelemetryNames.ActivitySourceName), "host subscribes to the Armor activity source");
                        Check.Equal("127.0.0.1", mapped.Prometheus.Hostname, "scrape endpoint binds loopback");

                        using (TelemetryHost host = TelemetryHost.Start(settings, "test", port))
                        {
                            Check.True(host.IsExporting, "enabled host is exporting");
                            Check.NotNull(host.PrometheusUrl, "scrape URL reported");
                            ArmorLog.Info("telemetry host test line {with braces}");

                            using (TempWorkspace ws = new TempWorkspace())
                            using (EngineFixture fx = await EngineFixture.BuildAsync(ws, ct).ConfigureAwait(false))
                            {
                                string source = Path.Combine(ws.RootDirectory, "source");
                                WriteFile(source, "a.bin", Content(14, 9000));
                                await new BackupEngine(fx.Database).RunAsync(NewPolicy(source), fx.Repository, fx.StorageTargetId, fx.EncryptionKey, fx.DataKey, fx.Chunking, BackupTypeEnum.Full, ct).ConfigureAwait(false);
                            }

                            string body;
                            using (HttpClient client = new HttpClient())
                            {
                                client.Timeout = TimeSpan.FromSeconds(10);
                                body = await client.GetStringAsync(host.PrometheusUrl, ct).ConfigureAwait(false);
                            }
                            Check.True(body.Contains("armor_backup_jobs_total"), "scrape exposes armor_backup_jobs_total");
                            Check.True(body.Contains("armor_backup_stage_duration_seconds_bucket"), "scrape exposes stage duration buckets");
                            Check.True(body.Contains("armor_storage_operations_total"), "scrape exposes storage operations");
                            Check.True(body.Contains("armor_build_info"), "scrape exposes build info");
                        }

                        TelemetrySettings conflict = new TelemetrySettings();
                        conflict.Enabled = true;
                        conflict.OtlpEnabled = false;
                        conflict.PrometheusEnabled = true;
                        using (TcpListener occupied = new TcpListener(IPAddress.Loopback, 0))
                        {
                            occupied.Start();
                            int busy = ((IPEndPoint)occupied.LocalEndpoint).Port;
                            using (TelemetryHost failed = TelemetryHost.Start(conflict, "test", busy))
                                Check.NotNull(failed, "a busy scrape port never throws out of Start");
                            occupied.Stop();
                        }
                    }),
                });
        }

        private static TestCaseDescriptor Case(string caseId, string displayName, Func<CancellationToken, Task> body)
        {
            return new TestCaseDescriptor(suiteId: "Telemetry", caseId: caseId, displayName: displayName, executeAsync: body);
        }

        private static TestCaseDescriptor Sync(string caseId, string displayName, Action body)
        {
            return new TestCaseDescriptor(suiteId: "Telemetry", caseId: caseId, displayName: displayName, executeAsync: ct =>
            {
                body();
                return Task.CompletedTask;
            });
        }

        private static Policy NewPolicy(string source)
        {
            Policy policy = new Policy();
            policy.Name = "telemetry-policy";
            policy.IncludePaths.Add(source);
            policy.StorageTargetId = "tgt_test";
            return policy;
        }

        private static void WriteFile(string root, string relative, byte[] content)
        {
            string full = Path.Combine(root, relative.Replace('/', Path.DirectorySeparatorChar));
            string? directory = Path.GetDirectoryName(full);
            if (!String.IsNullOrEmpty(directory))
                Directory.CreateDirectory(directory);
            File.WriteAllBytes(full, content);
        }

        private static byte[] Content(int seed, int length)
        {
            byte[] data = new byte[length];
            ulong state = (ulong)(seed * 2654435761U) + 0x9E3779B97F4A7C15UL;
            for (int i = 0; i < length; i++)
            {
                state ^= state << 13;
                state ^= state >> 7;
                state ^= state << 17;
                data[i] = (byte)(state & 0xFF);
            }
            return data;
        }

        private static int FreePort()
        {
            using (TcpListener listener = new TcpListener(IPAddress.Loopback, 0))
            {
                listener.Start();
                int port = ((IPEndPoint)listener.LocalEndpoint).Port;
                listener.Stop();
                return port;
            }
        }
    }
}
