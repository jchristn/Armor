namespace Armor.Core.Telemetry
{
    /// <summary>
    /// Every telemetry name Armor emits, in one place: the meter and activity-source names, each
    /// instrument name, the span names, the attribute (label) keys, and the bounded label values. These
    /// strings are a public contract consumed by Grafana dashboards and alert rules, so treat a rename as
    /// a breaking change. Instrument names are dotted OpenTelemetry names; a Prometheus exporter rewrites
    /// them to snake case and appends the unit and <c>_total</c> suffixes (for example
    /// <c>armor.backup.duration</c> becomes <c>armor_backup_duration_seconds</c>).
    /// </summary>
    public static class TelemetryNames
    {
        #region Sources

        /// <summary>
        /// Name of the <see cref="System.Diagnostics.Metrics.Meter"/> every Armor instrument is created on.
        /// </summary>
        public const string MeterName = "Armor";

        /// <summary>
        /// Name of the <see cref="System.Diagnostics.ActivitySource"/> every Armor span is started on.
        /// </summary>
        public const string ActivitySourceName = "Armor";

        #endregion

        #region Backup-Metrics

        /// <summary>Counter of backup jobs by type and outcome.</summary>
        public const string BackupJobs = "armor.backup.jobs";

        /// <summary>Histogram of end-to-end backup job duration, in seconds.</summary>
        public const string BackupDuration = "armor.backup.duration";

        /// <summary>Up-down counter of backup jobs currently running.</summary>
        public const string BackupJobsActive = "armor.backup.jobs.active";

        /// <summary>Counter of backup pipeline stage executions by stage and outcome.</summary>
        public const string BackupStage = "armor.backup.stage";

        /// <summary>Histogram of backup pipeline stage duration, in seconds.</summary>
        public const string BackupStageDuration = "armor.backup.stage.duration";

        /// <summary>Histogram of per-file and per-chunk work inside the copy stage (queued, read, hash, frame, dedupe_check, upload, commit), in seconds.</summary>
        public const string BackupFileStageDuration = "armor.backup.file.stage.duration";

        /// <summary>Counter of files settled by the copy stage, by file outcome.</summary>
        public const string BackupFiles = "armor.backup.files";

        /// <summary>Counter of files found by the source scan.</summary>
        public const string BackupFilesScanned = "armor.backup.files.scanned";

        /// <summary>Counter of backup bytes by kind (scanned, processed, stored, deduplicated, skipped).</summary>
        public const string BackupBytes = "armor.backup.bytes";

        /// <summary>Counter of chunk decisions by outcome (written or one of the deduplication paths).</summary>
        public const string BackupChunks = "armor.backup.chunks";

        /// <summary>Up-down counter of copy-stage workers currently processing a file.</summary>
        public const string BackupWorkersActive = "armor.backup.workers.active";

        /// <summary>Up-down counter of copy-stage worker slots provisioned by running jobs.</summary>
        public const string BackupWorkersCapacity = "armor.backup.workers.capacity";

        /// <summary>Up-down counter of files waiting in the copy-stage hand-off queue.</summary>
        public const string BackupQueueDepth = "armor.backup.queue.depth";

        /// <summary>Up-down counter of copy-stage hand-off queue capacity provisioned by running jobs.</summary>
        public const string BackupQueueCapacity = "armor.backup.queue.capacity";

        /// <summary>Counter of backup requests refused because the policy's run lock was already held.</summary>
        public const string BackupLockRejections = "armor.backup.lock.rejections";

        /// <summary>Gauge of the Unix time, in seconds, of the most recent successful backup seen by this process.</summary>
        public const string BackupLastSuccess = "armor.backup.last_success.timestamp";

        #endregion

        #region Restore-Verify-Retention-Metrics

        /// <summary>Counter of restore jobs by outcome.</summary>
        public const string RestoreJobs = "armor.restore.jobs";

        /// <summary>Histogram of end-to-end restore job duration, in seconds.</summary>
        public const string RestoreDuration = "armor.restore.duration";

        /// <summary>Up-down counter of restore jobs currently running.</summary>
        public const string RestoreJobsActive = "armor.restore.jobs.active";

        /// <summary>Counter of restore pipeline stage executions by stage and outcome.</summary>
        public const string RestoreStage = "armor.restore.stage";

        /// <summary>Histogram of restore pipeline stage duration, in seconds.</summary>
        public const string RestoreStageDuration = "armor.restore.stage.duration";

        /// <summary>Histogram of per-file and per-chunk restore work (download, decrypt, write), in seconds.</summary>
        public const string RestoreFileStageDuration = "armor.restore.file.stage.duration";

        /// <summary>Counter of files restored.</summary>
        public const string RestoreFiles = "armor.restore.files";

        /// <summary>Counter of plaintext bytes restored.</summary>
        public const string RestoreBytes = "armor.restore.bytes";

        /// <summary>Counter of verification runs by outcome.</summary>
        public const string VerifyRuns = "armor.verify.runs";

        /// <summary>Histogram of verification run duration, in seconds.</summary>
        public const string VerifyDuration = "armor.verify.duration";

        /// <summary>Counter of chunks checked by verification, by chunk outcome (verified, missing, corrupt).</summary>
        public const string VerifyChunks = "armor.verify.chunks";

        /// <summary>Counter of retention runs by outcome.</summary>
        public const string RetentionRuns = "armor.retention.runs";

        /// <summary>Histogram of retention run duration, in seconds.</summary>
        public const string RetentionDuration = "armor.retention.duration";

        /// <summary>Counter of retention stage executions by stage and outcome.</summary>
        public const string RetentionStage = "armor.retention.stage";

        /// <summary>Histogram of retention stage duration (prune, sweep), in seconds.</summary>
        public const string RetentionStageDuration = "armor.retention.stage.duration";

        /// <summary>Counter of points-in-time pruned by retention.</summary>
        public const string RetentionJobsPruned = "armor.retention.jobs.pruned";

        /// <summary>Counter of unreferenced chunks deleted by retention.</summary>
        public const string RetentionChunksDeleted = "armor.retention.chunks.deleted";

        #endregion

        #region Scheduler-Metrics

        /// <summary>Counter of scheduler ticks by outcome.</summary>
        public const string SchedulerTicks = "armor.scheduler.ticks";

        /// <summary>Histogram of scheduler tick duration (including any backups the tick ran), in seconds.</summary>
        public const string SchedulerTickDuration = "armor.scheduler.tick.duration";

        /// <summary>Counter of per-schedule decisions taken by a tick.</summary>
        public const string SchedulerDecisions = "armor.scheduler.decisions";

        /// <summary>Gauge of schedules that were due on the last tick but were left due (not run).</summary>
        public const string SchedulerSchedulesPending = "armor.scheduler.schedules.pending";

        /// <summary>Gauge of the Unix time, in seconds, the last scheduler tick finished.</summary>
        public const string SchedulerLastTick = "armor.scheduler.last_tick.timestamp";

        #endregion

        #region Integration-Metrics

        /// <summary>Counter of storage-target operations by target type, operation, and outcome.</summary>
        public const string StorageOperations = "armor.storage.operations";

        /// <summary>Histogram of storage-target operation latency, in seconds.</summary>
        public const string StorageOperationDuration = "armor.storage.operation.duration";

        /// <summary>Up-down counter of storage-target operations currently in flight.</summary>
        public const string StorageOperationsActive = "armor.storage.operations.active";

        /// <summary>Counter of bytes moved to or from storage targets, by direction.</summary>
        public const string StorageBytes = "armor.storage.bytes";

        /// <summary>Counter of key operations (provision, unlock) by method and outcome.</summary>
        public const string KeyOperations = "armor.key.operations";

        /// <summary>Histogram of key operation duration (dominated by PBKDF2), in seconds.</summary>
        public const string KeyOperationDuration = "armor.key.operation.duration";

        /// <summary>Counter of disaster-recovery operations by operation and outcome.</summary>
        public const string RecoveryOperations = "armor.recovery.operations";

        /// <summary>Histogram of disaster-recovery operation duration, in seconds.</summary>
        public const string RecoveryOperationDuration = "armor.recovery.operation.duration";

        /// <summary>Counter of interrupted backup jobs reconciled at startup.</summary>
        public const string StartupReconciledJobs = "armor.startup.reconciled_jobs";

        /// <summary>Counter of errors by component and error type.</summary>
        public const string Errors = "armor.errors";

        #endregion

        #region Process-Metrics

        /// <summary>Gauge (always 1) carrying build and process identity as labels.</summary>
        public const string BuildInfo = "armor.build.info";

        /// <summary>Gauge of the configured scheduler tick interval, in seconds.</summary>
        public const string ConfigSchedulerTickInterval = "armor.config.scheduler.tick_interval";

        /// <summary>Gauge of the configured content-defined chunk size bounds, in bytes.</summary>
        public const string ConfigChunkSize = "armor.config.chunk.size";

        /// <summary>Gauge (1 or 0) of whether per-chunk storage spans are enabled.</summary>
        public const string ConfigTraceChunkOperations = "armor.config.trace_chunk_operations";

        #endregion

        #region Span-Names

        /// <summary>Root span of a backup request (policy resolution, run lock, engine run, retention).</summary>
        public const string SpanBackupRun = "backup.run";

        /// <summary>Span of one backup engine job; its children are the <c>stage:</c> spans.</summary>
        public const string SpanBackupJob = "backup.job";

        /// <summary>Span of one restore engine job.</summary>
        public const string SpanRestoreJob = "restore.job";

        /// <summary>Span of one verification run.</summary>
        public const string SpanVerifyJob = "verify.job";

        /// <summary>Span of one retention run.</summary>
        public const string SpanRetentionRun = "retention.run";

        /// <summary>Root span of one scheduler tick.</summary>
        public const string SpanSchedulerTick = "scheduler.tick";

        /// <summary>Span of one key operation.</summary>
        public const string SpanKeyOperation = "key.operation";

        /// <summary>Span of startup reconciliation of interrupted backups.</summary>
        public const string SpanStartupReconcile = "startup.reconcile";

        /// <summary>Prefix of a pipeline stage span; the full name is <c>stage:&lt;name&gt;</c>.</summary>
        public const string SpanStagePrefix = "stage:";

        /// <summary>Prefix of a disaster-recovery span; the full name is <c>recovery.&lt;operation&gt;</c>.</summary>
        public const string SpanRecoveryPrefix = "recovery.";

        #endregion

        #region Attribute-Keys

        /// <summary>Outcome label: one of <see cref="OutcomeSuccess"/>, <see cref="OutcomeFailure"/>, <see cref="OutcomeCanceled"/>.</summary>
        public const string AttrOutcome = "armor.outcome";

        /// <summary>OpenTelemetry semantic-convention error type: the exception's type name.</summary>
        public const string AttrErrorType = "error.type";

        /// <summary>Backup type label: full, incremental, or differential.</summary>
        public const string AttrBackupType = "armor.backup.type";

        /// <summary>Pipeline stage label.</summary>
        public const string AttrStage = "armor.stage";

        /// <summary>File outcome label for <see cref="BackupFiles"/>.</summary>
        public const string AttrFileOutcome = "armor.file.outcome";

        /// <summary>Byte kind label for <see cref="BackupBytes"/>.</summary>
        public const string AttrBytesKind = "armor.bytes.kind";

        /// <summary>Chunk outcome label.</summary>
        public const string AttrChunkOutcome = "armor.chunk.outcome";

        /// <summary>Storage target type label (disk, amazon_s3, azure_blob, google_cloud, cifs, nfs).</summary>
        public const string AttrStorageType = "armor.storage.type";

        /// <summary>Storage operation label.</summary>
        public const string AttrStorageOperation = "armor.storage.operation";

        /// <summary>Storage byte direction label: sent or received.</summary>
        public const string AttrStorageDirection = "armor.storage.direction";

        /// <summary>Scheduler decision label.</summary>
        public const string AttrSchedulerDecision = "armor.scheduler.decision";

        /// <summary>Key operation label: provision, unlock_passphrase, or unlock_keyfile.</summary>
        public const string AttrKeyOperation = "armor.key.operation";

        /// <summary>Disaster-recovery operation label.</summary>
        public const string AttrRecoveryOperation = "armor.recovery.operation";

        /// <summary>Component label for <see cref="Errors"/>.</summary>
        public const string AttrComponent = "armor.component";

        /// <summary>Chunk size bound label for <see cref="ConfigChunkSize"/>: min, avg, or max.</summary>
        public const string AttrChunkBound = "armor.chunk.bound";

        /// <summary>Armor version label on <see cref="BuildInfo"/>.</summary>
        public const string AttrVersion = "armor.version";

        /// <summary>Process role label on <see cref="BuildInfo"/> (the entry assembly name).</summary>
        public const string AttrProcess = "armor.process";

        /// <summary>Operating-system family label on <see cref="BuildInfo"/>.</summary>
        public const string AttrOsType = "os.type";

        /// <summary>.NET runtime version label on <see cref="BuildInfo"/>.</summary>
        public const string AttrRuntimeVersion = "process.runtime.version";

        /// <summary>Span attribute: policy identifier. Spans only; never a metric label.</summary>
        public const string AttrPolicyId = "armor.policy.id";

        /// <summary>Span attribute: backup job identifier. Spans only; never a metric label.</summary>
        public const string AttrJobId = "armor.job.id";

        /// <summary>Span attribute: restore job identifier. Spans only; never a metric label.</summary>
        public const string AttrRestoreJobId = "armor.restore_job.id";

        /// <summary>Span attribute: schedule identifier. Spans only; never a metric label.</summary>
        public const string AttrScheduleId = "armor.schedule.id";

        /// <summary>Span attribute: storage target identifier. Spans only; never a metric label.</summary>
        public const string AttrStorageTargetId = "armor.storage_target.id";

        /// <summary>Span attribute: repository object key. Spans only; never a metric label.</summary>
        public const string AttrStorageKey = "armor.storage.key";

        /// <summary>Span attribute: whether a backup job resumed an interrupted run.</summary>
        public const string AttrResumed = "armor.backup.resumed";

        /// <summary>Span attribute: file count at the end of a job.</summary>
        public const string AttrFileCount = "armor.files";

        /// <summary>Span attribute: byte count at the end of a job.</summary>
        public const string AttrByteCount = "armor.bytes";

        /// <summary>Span attribute: chunks written by a backup job.</summary>
        public const string AttrChunksWritten = "armor.chunks.written";

        /// <summary>Span attribute: chunks reused by a backup job.</summary>
        public const string AttrChunksReused = "armor.chunks.reused";

        /// <summary>Span attribute: files skipped as unreadable by a backup job.</summary>
        public const string AttrFilesSkipped = "armor.files.skipped";

        /// <summary>Span attribute: copy-stage worker count.</summary>
        public const string AttrWorkers = "armor.workers";

        /// <summary>Span attribute: schedules that ran on a tick.</summary>
        public const string AttrSchedulesRan = "armor.schedules.ran";

        #endregion

        #region Label-Values

        /// <summary>Outcome: the operation succeeded.</summary>
        public const string OutcomeSuccess = "success";

        /// <summary>Outcome: the operation failed.</summary>
        public const string OutcomeFailure = "failure";

        /// <summary>Outcome: the operation was canceled.</summary>
        public const string OutcomeCanceled = "canceled";

        /// <summary>Backup stage: find or create the job row (resume detection, baseline resolution).</summary>
        public const string StageOpen = "open";

        /// <summary>Backup stage: resolve the policy, key, and target and probe the repository header.</summary>
        public const string StagePrepare = "prepare";

        /// <summary>Backup stage: write the repository header.</summary>
        public const string StageHeader = "header";

        /// <summary>Backup stage: load the baseline manifest for an incremental or differential run.</summary>
        public const string StageBaseline = "baseline";

        /// <summary>Backup stage: enumerate the source into the work list.</summary>
        public const string StageScan = "scan";

        /// <summary>Backup stage: the parallel copy of the work list (read, chunk, hash, frame, upload).</summary>
        public const string StageProcess = "process";

        /// <summary>Backup stage: assemble and write the segmented manifest.</summary>
        public const string StageManifest = "manifest";

        /// <summary>Backup and restore stage: write the run sidecar and settle the job row.</summary>
        public const string StageFinalize = "finalize";

        /// <summary>Backup stage: apply retention after a successful run.</summary>
        public const string StageRetention = "retention";

        /// <summary>Restore stage: stream the manifest and write the in-scope files.</summary>
        public const string StageRestore = "restore";

        /// <summary>Retention stage: prune expired points-in-time.</summary>
        public const string StagePrune = "prune";

        /// <summary>Retention stage: delete unreferenced chunks.</summary>
        public const string StageSweep = "sweep";

        /// <summary>File stage: time a file waited in the hand-off queue for a free worker.</summary>
        public const string FileStageQueued = "queued";

        /// <summary>File stage: reading and content-defined chunking of the source.</summary>
        public const string FileStageRead = "read";

        /// <summary>File stage: SHA-256 of a chunk.</summary>
        public const string FileStageHash = "hash";

        /// <summary>File stage: compression plus AES-256-GCM encryption of a chunk.</summary>
        public const string FileStageFrame = "frame";

        /// <summary>File stage: checking the target for an already-stored chunk.</summary>
        public const string FileStageDedupeCheck = "dedupe_check";

        /// <summary>File stage: writing a new chunk to the target.</summary>
        public const string FileStageUpload = "upload";

        /// <summary>File stage: committing a file's chunk references and done-mark to the database.</summary>
        public const string FileStageCommit = "commit";

        /// <summary>Restore file stage: reading a chunk from the target.</summary>
        public const string FileStageDownload = "download";

        /// <summary>Restore file stage: decrypting, authenticating, and decompressing a chunk.</summary>
        public const string FileStageDecrypt = "decrypt";

        /// <summary>Restore file stage: writing plaintext to the destination file.</summary>
        public const string FileStageWrite = "write";

        /// <summary>File outcome: the file was read and its new chunks written.</summary>
        public const string FileOutcomeCopied = "copied";

        /// <summary>File outcome: the file was unchanged and its chunks reused from the baseline.</summary>
        public const string FileOutcomeUnchanged = "unchanged";

        /// <summary>File outcome: the file could not be read and was skipped.</summary>
        public const string FileOutcomeSkipped = "skipped";

        /// <summary>File outcome: the file disappeared between scan and copy.</summary>
        public const string FileOutcomeVanished = "vanished";

        /// <summary>Byte kind: bytes found by the scan.</summary>
        public const string BytesScanned = "scanned";

        /// <summary>Byte kind: plaintext bytes of files committed to the manifest.</summary>
        public const string BytesProcessed = "processed";

        /// <summary>Byte kind: bytes written to the target after compression and encryption.</summary>
        public const string BytesStored = "stored";

        /// <summary>Byte kind: plaintext bytes that did not need to be written because they were already stored.</summary>
        public const string BytesDeduplicated = "deduplicated";

        /// <summary>Byte kind: bytes of files skipped as unreadable.</summary>
        public const string BytesSkipped = "skipped";

        /// <summary>Chunk outcome: a new chunk was written to the target.</summary>
        public const string ChunkWritten = "written";

        /// <summary>Chunk outcome: already written earlier in the same run (in-run cache hit).</summary>
        public const string ChunkDeduplicatedRun = "deduplicated_run";

        /// <summary>Chunk outcome: already present on the target from an earlier run.</summary>
        public const string ChunkDeduplicatedTarget = "deduplicated_target";

        /// <summary>Chunk outcome: reused from the baseline manifest because the file was unchanged.</summary>
        public const string ChunkDeduplicatedBaseline = "deduplicated_baseline";

        /// <summary>Verify chunk outcome: the chunk was present and authenticated.</summary>
        public const string ChunkVerified = "verified";

        /// <summary>Verify chunk outcome: the chunk was missing from the target.</summary>
        public const string ChunkMissing = "missing";

        /// <summary>Verify chunk outcome: the chunk failed authentication or decoding.</summary>
        public const string ChunkCorrupt = "corrupt";

        /// <summary>Storage direction: bytes sent to the target.</summary>
        public const string DirectionSent = "sent";

        /// <summary>Storage direction: bytes received from the target.</summary>
        public const string DirectionReceived = "received";

        /// <summary>Storage operation: connection validation probe.</summary>
        public const string OpValidate = "validate";

        /// <summary>Storage operation: write a metadata object.</summary>
        public const string OpWriteObject = "write_object";

        /// <summary>Storage operation: read a metadata object.</summary>
        public const string OpReadObject = "read_object";

        /// <summary>Storage operation: test for a metadata object.</summary>
        public const string OpObjectExists = "object_exists";

        /// <summary>Storage operation: delete a metadata object.</summary>
        public const string OpDeleteObject = "delete_object";

        /// <summary>Storage operation: enumerate keys under a prefix.</summary>
        public const string OpEnumerate = "enumerate";

        /// <summary>Storage operation: write a chunk.</summary>
        public const string OpWriteChunk = "write_chunk";

        /// <summary>Storage operation: read a chunk.</summary>
        public const string OpReadChunk = "read_chunk";

        /// <summary>Storage operation: test for a chunk.</summary>
        public const string OpChunkExists = "chunk_exists";

        /// <summary>Storage operation: delete a chunk.</summary>
        public const string OpDeleteChunk = "delete_chunk";

        /// <summary>Scheduler decision: the schedule's backup ran and succeeded.</summary>
        public const string DecisionRan = "ran";

        /// <summary>Scheduler decision: the schedule is disabled.</summary>
        public const string DecisionDisabled = "disabled";

        /// <summary>Scheduler decision: the schedule had no next run yet and was initialized.</summary>
        public const string DecisionInitialized = "initialized";

        /// <summary>Scheduler decision: the schedule is not due.</summary>
        public const string DecisionNotDue = "not_due";

        /// <summary>Scheduler decision: the policy is missing or disabled, so the run was skipped forward.</summary>
        public const string DecisionPolicyDisabled = "policy_disabled";

        /// <summary>Scheduler decision: the policy's data key is not available for unattended use (left due).</summary>
        public const string DecisionKeyUnavailable = "key_unavailable";

        /// <summary>Scheduler decision: the target is not reachable right now (left due).</summary>
        public const string DecisionTargetUnreachable = "target_unreachable";

        /// <summary>Scheduler decision: the policy is already running (left due).</summary>
        public const string DecisionAlreadyRunning = "already_running";

        /// <summary>Scheduler decision: the backup failed (left due).</summary>
        public const string DecisionFailed = "failed";

        /// <summary>Scheduler decision: a recent failure is still backing off (left due).</summary>
        public const string DecisionBackoff = "backoff";

        /// <summary>Key operation: provision a new key.</summary>
        public const string KeyOpProvision = "provision";

        /// <summary>Key operation: unlock with a passphrase.</summary>
        public const string KeyOpUnlockPassphrase = "unlock_passphrase";

        /// <summary>Key operation: unlock with a key file.</summary>
        public const string KeyOpUnlockKeyFile = "unlock_keyfile";

        /// <summary>Recovery operation: open a recovery session against a target.</summary>
        public const string RecoveryOpen = "open";

        /// <summary>Recovery operation: list the points-in-time on a target.</summary>
        public const string RecoveryBrowse = "browse";

        /// <summary>Recovery operation: list the folders of a point-in-time.</summary>
        public const string RecoveryListFolders = "list_folders";

        /// <summary>Recovery operation: list the files of a point-in-time.</summary>
        public const string RecoveryListFiles = "list_files";

        /// <summary>Error component: the backup pipeline.</summary>
        public const string ComponentBackup = "backup";

        /// <summary>Error component: reading a source file (the file is skipped, the run continues).</summary>
        public const string ComponentSource = "source";

        /// <summary>Error component: the restore pipeline.</summary>
        public const string ComponentRestore = "restore";

        /// <summary>Error component: verification.</summary>
        public const string ComponentVerify = "verify";

        /// <summary>Error component: retention.</summary>
        public const string ComponentRetention = "retention";

        /// <summary>Error component: the scheduler loop.</summary>
        public const string ComponentScheduler = "scheduler";

        /// <summary>Error component: a storage-target call.</summary>
        public const string ComponentStorage = "storage";

        /// <summary>Error component: a key operation.</summary>
        public const string ComponentKey = "key";

        /// <summary>Error component: disaster recovery.</summary>
        public const string ComponentRecovery = "recovery";

        #endregion
    }
}
