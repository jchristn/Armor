namespace Armor.Core.Telemetry
{
    using System;
    using System.Collections.Generic;
    using System.Diagnostics;
    using System.Diagnostics.Metrics;
    using System.Reflection;
    using System.Runtime.InteropServices;
    using System.Threading;
    using Armor.Core.Configuration;
    using Armor.Core.Enums;

    /// <summary>
    /// Armor's telemetry surface: one <see cref="System.Diagnostics.Metrics.Meter"/> and one
    /// <see cref="System.Diagnostics.ActivitySource"/>, both named <see cref="TelemetryNames.MeterName"/>,
    /// plus every instrument the engine records into. Emission rides the base class library only, so it
    /// costs next to nothing and allocates nothing until a host (Radiant, an OpenTelemetry SDK,
    /// dotnet-counters, or a test listener) subscribes to the names. Every recording helper is
    /// best-effort: a failing listener is swallowed so instrumentation can never break a backup.
    /// All members are thread-safe.
    /// </summary>
    public static class ArmorTelemetry
    {
        #region Public-Members

        /// <summary>
        /// The Armor meter. Subscribe to <see cref="TelemetryNames.MeterName"/> to collect its instruments.
        /// </summary>
        public static Meter Meter
        {
            get { return _Meter; }
        }

        /// <summary>
        /// The Armor activity source. Subscribe to <see cref="TelemetryNames.ActivitySourceName"/> to collect its spans.
        /// </summary>
        public static ActivitySource ActivitySource
        {
            get { return _Source; }
        }

        /// <summary>
        /// Whether every chunk-level storage call (write, read, exists, delete of a chunk) gets its own
        /// client span. Default is false: a large backup makes millions of chunk calls, which would flood
        /// a trace backend, so chunk calls are covered by metrics only and metadata-object calls get spans.
        /// Turn this on briefly to see individual chunk latencies in a trace.
        /// </summary>
        public static bool TraceChunkOperations
        {
            get { return _TraceChunkOperations; }
            set { _TraceChunkOperations = value; }
        }

        #endregion

        #region Internal-Members

        internal static readonly Counter<long> BackupJobs;
        internal static readonly Histogram<double> BackupDuration;
        internal static readonly UpDownCounter<long> BackupJobsActive;
        internal static readonly Counter<long> BackupStage;
        internal static readonly Histogram<double> BackupStageDuration;
        internal static readonly Histogram<double> BackupFileStageDuration;
        internal static readonly Counter<long> BackupFiles;
        internal static readonly Counter<long> BackupFilesScanned;
        internal static readonly Counter<long> BackupBytes;
        internal static readonly Counter<long> BackupChunks;
        internal static readonly UpDownCounter<long> BackupWorkersActive;
        internal static readonly UpDownCounter<long> BackupWorkersCapacity;
        internal static readonly UpDownCounter<long> BackupQueueDepth;
        internal static readonly UpDownCounter<long> BackupQueueCapacity;
        internal static readonly Counter<long> BackupLockRejections;

        internal static readonly Counter<long> RestoreJobs;
        internal static readonly Histogram<double> RestoreDuration;
        internal static readonly UpDownCounter<long> RestoreJobsActive;
        internal static readonly Counter<long> RestoreStage;
        internal static readonly Histogram<double> RestoreStageDuration;
        internal static readonly Histogram<double> RestoreFileStageDuration;
        internal static readonly Counter<long> RestoreFiles;
        internal static readonly Counter<long> RestoreBytes;

        internal static readonly Counter<long> VerifyRuns;
        internal static readonly Histogram<double> VerifyDuration;
        internal static readonly Counter<long> VerifyChunks;

        internal static readonly Counter<long> RetentionRuns;
        internal static readonly Histogram<double> RetentionDuration;
        internal static readonly Counter<long> RetentionStage;
        internal static readonly Histogram<double> RetentionStageDuration;
        internal static readonly Counter<long> RetentionJobsPruned;
        internal static readonly Counter<long> RetentionChunksDeleted;

        internal static readonly Counter<long> SchedulerTicks;
        internal static readonly Histogram<double> SchedulerTickDuration;
        internal static readonly Counter<long> SchedulerDecisions;

        internal static readonly Counter<long> StorageOperations;
        internal static readonly Histogram<double> StorageOperationDuration;
        internal static readonly UpDownCounter<long> StorageOperationsActive;
        internal static readonly Counter<long> StorageBytes;

        internal static readonly Counter<long> KeyOperations;
        internal static readonly Histogram<double> KeyOperationDuration;

        internal static readonly Counter<long> RecoveryOperations;
        internal static readonly Histogram<double> RecoveryOperationDuration;

        internal static readonly Counter<long> StartupReconciledJobs;
        internal static readonly Counter<long> Errors;

        #endregion

        #region Private-Members

        // Bucket boundaries, in seconds, handed to the collector as advice. Without them an OpenTelemetry
        // SDK falls back to its millisecond-oriented defaults (0, 5, 10, 25 ...), which put every Armor
        // measurement in the first one or two buckets.
        private static readonly double[] _JobBuckets = new double[] { 0.1, 0.5, 1, 5, 10, 30, 60, 120, 300, 600, 1800, 3600, 7200, 14400, 28800, 86400 };
        private static readonly double[] _OperationBuckets = new double[] { 0.001, 0.0025, 0.005, 0.01, 0.025, 0.05, 0.1, 0.25, 0.5, 1, 2.5, 5, 10, 30, 60 };
        private static readonly double[] _FineBuckets = new double[] { 0.00005, 0.0001, 0.00025, 0.0005, 0.001, 0.0025, 0.005, 0.01, 0.025, 0.05, 0.1, 0.25, 0.5, 1, 5, 30 };

        private static readonly string _Version = ResolveVersion();
        private static readonly Meter _Meter = new Meter(TelemetryNames.MeterName, _Version);
        private static readonly ActivitySource _Source = new ActivitySource(TelemetryNames.ActivitySourceName, _Version);

        private static volatile bool _TraceChunkOperations = false;
        private static volatile ArmorSettings? _Settings = null;
        private static long _LastBackupSuccessUnixMs = 0;
        private static long _LastSchedulerTickUnixMs = 0;
        private static long _SchedulesPending = -1;

        #endregion

        #region Constructors-and-Factories

        static ArmorTelemetry()
        {
            BackupJobs = _Meter.CreateCounter<long>(TelemetryNames.BackupJobs, "{job}", "Backup jobs by type and outcome.");
            BackupDuration = Histogram(TelemetryNames.BackupDuration, "End-to-end backup job duration.", _JobBuckets);
            BackupJobsActive = _Meter.CreateUpDownCounter<long>(TelemetryNames.BackupJobsActive, "{job}", "Backup jobs currently running.");
            BackupStage = _Meter.CreateCounter<long>(TelemetryNames.BackupStage, "{stage}", "Backup pipeline stage executions by stage and outcome.");
            BackupStageDuration = Histogram(TelemetryNames.BackupStageDuration, "Backup pipeline stage duration.", _JobBuckets);
            BackupFileStageDuration = Histogram(TelemetryNames.BackupFileStageDuration, "Per-file and per-chunk work inside the copy stage.", _FineBuckets);
            BackupFiles = _Meter.CreateCounter<long>(TelemetryNames.BackupFiles, "{file}", "Files settled by the copy stage, by outcome.");
            BackupFilesScanned = _Meter.CreateCounter<long>(TelemetryNames.BackupFilesScanned, "{file}", "Files found by the source scan.");
            BackupBytes = _Meter.CreateCounter<long>(TelemetryNames.BackupBytes, "By", "Backup bytes by kind.");
            BackupChunks = _Meter.CreateCounter<long>(TelemetryNames.BackupChunks, "{chunk}", "Chunk decisions by outcome.");
            BackupWorkersActive = _Meter.CreateUpDownCounter<long>(TelemetryNames.BackupWorkersActive, "{worker}", "Copy-stage workers currently processing a file.");
            BackupWorkersCapacity = _Meter.CreateUpDownCounter<long>(TelemetryNames.BackupWorkersCapacity, "{worker}", "Copy-stage worker slots provisioned by running jobs.");
            BackupQueueDepth = _Meter.CreateUpDownCounter<long>(TelemetryNames.BackupQueueDepth, "{file}", "Files waiting in the copy-stage hand-off queue.");
            BackupQueueCapacity = _Meter.CreateUpDownCounter<long>(TelemetryNames.BackupQueueCapacity, "{file}", "Copy-stage hand-off queue capacity provisioned by running jobs.");
            BackupLockRejections = _Meter.CreateCounter<long>(TelemetryNames.BackupLockRejections, "{rejection}", "Backup requests refused because the policy run lock was held.");

            RestoreJobs = _Meter.CreateCounter<long>(TelemetryNames.RestoreJobs, "{job}", "Restore jobs by outcome.");
            RestoreDuration = Histogram(TelemetryNames.RestoreDuration, "End-to-end restore job duration.", _JobBuckets);
            RestoreJobsActive = _Meter.CreateUpDownCounter<long>(TelemetryNames.RestoreJobsActive, "{job}", "Restore jobs currently running.");
            RestoreStage = _Meter.CreateCounter<long>(TelemetryNames.RestoreStage, "{stage}", "Restore pipeline stage executions by stage and outcome.");
            RestoreStageDuration = Histogram(TelemetryNames.RestoreStageDuration, "Restore pipeline stage duration.", _JobBuckets);
            RestoreFileStageDuration = Histogram(TelemetryNames.RestoreFileStageDuration, "Per-file and per-chunk restore work.", _FineBuckets);
            RestoreFiles = _Meter.CreateCounter<long>(TelemetryNames.RestoreFiles, "{file}", "Files restored.");
            RestoreBytes = _Meter.CreateCounter<long>(TelemetryNames.RestoreBytes, "By", "Plaintext bytes restored.");

            VerifyRuns = _Meter.CreateCounter<long>(TelemetryNames.VerifyRuns, "{run}", "Verification runs by outcome.");
            VerifyDuration = Histogram(TelemetryNames.VerifyDuration, "Verification run duration.", _JobBuckets);
            VerifyChunks = _Meter.CreateCounter<long>(TelemetryNames.VerifyChunks, "{chunk}", "Chunks checked by verification, by outcome.");

            RetentionRuns = _Meter.CreateCounter<long>(TelemetryNames.RetentionRuns, "{run}", "Retention runs by outcome.");
            RetentionDuration = Histogram(TelemetryNames.RetentionDuration, "Retention run duration.", _JobBuckets);
            RetentionStage = _Meter.CreateCounter<long>(TelemetryNames.RetentionStage, "{stage}", "Retention stage executions by stage and outcome.");
            RetentionStageDuration = Histogram(TelemetryNames.RetentionStageDuration, "Retention stage duration.", _JobBuckets);
            RetentionJobsPruned = _Meter.CreateCounter<long>(TelemetryNames.RetentionJobsPruned, "{job}", "Points-in-time pruned by retention.");
            RetentionChunksDeleted = _Meter.CreateCounter<long>(TelemetryNames.RetentionChunksDeleted, "{chunk}", "Unreferenced chunks deleted by retention.");

            SchedulerTicks = _Meter.CreateCounter<long>(TelemetryNames.SchedulerTicks, "{tick}", "Scheduler ticks by outcome.");
            SchedulerTickDuration = Histogram(TelemetryNames.SchedulerTickDuration, "Scheduler tick duration, including the backups it ran.", _JobBuckets);
            SchedulerDecisions = _Meter.CreateCounter<long>(TelemetryNames.SchedulerDecisions, "{decision}", "Per-schedule decisions taken by scheduler ticks.");

            StorageOperations = _Meter.CreateCounter<long>(TelemetryNames.StorageOperations, "{operation}", "Storage-target operations by target type, operation, and outcome.");
            StorageOperationDuration = Histogram(TelemetryNames.StorageOperationDuration, "Storage-target operation latency.", _OperationBuckets);
            StorageOperationsActive = _Meter.CreateUpDownCounter<long>(TelemetryNames.StorageOperationsActive, "{operation}", "Storage-target operations currently in flight.");
            StorageBytes = _Meter.CreateCounter<long>(TelemetryNames.StorageBytes, "By", "Bytes moved to or from storage targets.");

            KeyOperations = _Meter.CreateCounter<long>(TelemetryNames.KeyOperations, "{operation}", "Key operations by operation and outcome.");
            KeyOperationDuration = Histogram(TelemetryNames.KeyOperationDuration, "Key operation duration.", _OperationBuckets);

            RecoveryOperations = _Meter.CreateCounter<long>(TelemetryNames.RecoveryOperations, "{operation}", "Disaster-recovery operations by operation and outcome.");
            RecoveryOperationDuration = Histogram(TelemetryNames.RecoveryOperationDuration, "Disaster-recovery operation duration.", _JobBuckets);

            StartupReconciledJobs = _Meter.CreateCounter<long>(TelemetryNames.StartupReconciledJobs, "{job}", "Interrupted backup jobs reconciled at startup.");
            Errors = _Meter.CreateCounter<long>(TelemetryNames.Errors, "{error}", "Errors by component and error type.");

            _Meter.CreateObservableGauge<double>(TelemetryNames.BackupLastSuccess, ObserveLastBackupSuccess, "s", "Unix time of the most recent successful backup seen by this process.");
            _Meter.CreateObservableGauge<double>(TelemetryNames.SchedulerLastTick, ObserveLastSchedulerTick, "s", "Unix time the last scheduler tick finished.");
            _Meter.CreateObservableGauge<long>(TelemetryNames.SchedulerSchedulesPending, ObserveSchedulesPending, "{schedule}", "Schedules due on the last tick that were left due.");
            _Meter.CreateObservableGauge<long>(TelemetryNames.BuildInfo, ObserveBuildInfo, null, "Build and process identity; always 1.");
            _Meter.CreateObservableGauge<double>(TelemetryNames.ConfigSchedulerTickInterval, ObserveTickInterval, "s", "Configured scheduler tick interval.");
            _Meter.CreateObservableGauge<long>(TelemetryNames.ConfigChunkSize, ObserveChunkSizes, "By", "Configured content-defined chunk size bounds.");
            _Meter.CreateObservableGauge<long>(TelemetryNames.ConfigTraceChunkOperations, ObserveTraceChunkOperations, null, "Whether per-chunk storage spans are enabled (1) or not (0).");
        }

        #endregion

        #region Public-Methods

        /// <summary>
        /// Publish the effective configuration so the safe configuration gauges (scheduler interval,
        /// chunk bounds) report it. Called when a runtime context loads settings. No secrets are read.
        /// </summary>
        /// <param name="settings">The effective settings. Null clears the configuration gauges.</param>
        public static void ObserveSettings(ArmorSettings? settings)
        {
            _Settings = settings;
            if (settings != null)
                TraceChunkOperations = settings.Telemetry.TraceChunkOperations;
        }

        /// <summary>
        /// Record that a backup completed successfully at the given time, advancing the
        /// <see cref="TelemetryNames.BackupLastSuccess"/> gauge if it is newer than the current value. A host
        /// calls this at startup with the newest completed job from the database so the gauge survives a
        /// restart; the engine calls it after every successful run.
        /// </summary>
        /// <param name="completedUtc">When the backup completed, in UTC.</param>
        public static void NoteBackupSucceeded(DateTime completedUtc)
        {
            long value = new DateTimeOffset(DateTime.SpecifyKind(completedUtc, DateTimeKind.Utc)).ToUnixTimeMilliseconds();
            while (true)
            {
                long current = Interlocked.Read(ref _LastBackupSuccessUnixMs);
                if (value <= current)
                    return;
                if (Interlocked.CompareExchange(ref _LastBackupSuccessUnixMs, value, current) == current)
                    return;
            }
        }

        /// <summary>
        /// Start an Armor span. Returns null when nobody is listening or the span is not sampled, so callers
        /// use the null-conditional operator and carry on; never throws.
        /// </summary>
        /// <param name="name">The span name. Use a stable, low-cardinality name from <see cref="TelemetryNames"/>.</param>
        /// <param name="kind">The span kind. Default is <see cref="ActivityKind.Internal"/>.</param>
        /// <returns>The started activity, or null.</returns>
        public static Activity? StartActivity(string name, ActivityKind kind = ActivityKind.Internal)
        {
            try
            {
                return _Source.StartActivity(name, kind);
            }
            catch (Exception)
            {
                return null;
            }
        }

        /// <summary>
        /// The bounded <see cref="TelemetryNames.AttrStorageType"/> label value for a storage target type.
        /// </summary>
        /// <param name="type">The storage target type.</param>
        /// <returns>One of disk, amazon_s3, azure_blob, google_cloud, cifs, nfs, or other.</returns>
        public static string StorageTypeLabel(StorageTargetTypeEnum type)
        {
            switch (type)
            {
                case StorageTargetTypeEnum.Disk:
                    return "disk";
                case StorageTargetTypeEnum.AmazonS3:
                    return "amazon_s3";
                case StorageTargetTypeEnum.AzureBlob:
                    return "azure_blob";
                case StorageTargetTypeEnum.GoogleCloud:
                    return "google_cloud";
                case StorageTargetTypeEnum.Cifs:
                    return "cifs";
                case StorageTargetTypeEnum.Nfs:
                    return "nfs";
                default:
                    return "other";
            }
        }

        /// <summary>
        /// The bounded <see cref="TelemetryNames.AttrBackupType"/> label value for a backup type.
        /// </summary>
        /// <param name="type">The backup type.</param>
        /// <returns>full, incremental, differential, or other.</returns>
        public static string BackupTypeLabel(BackupTypeEnum type)
        {
            switch (type)
            {
                case BackupTypeEnum.Full:
                    return "full";
                case BackupTypeEnum.Incremental:
                    return "incremental";
                case BackupTypeEnum.Differential:
                    return "differential";
                default:
                    return "other";
            }
        }

        /// <summary>
        /// The <see cref="TelemetryNames.AttrErrorType"/> value for an exception: its type name, which is
        /// bounded by the code base rather than by input.
        /// </summary>
        /// <param name="exception">The exception. Null yields "unknown".</param>
        /// <returns>The error type.</returns>
        public static string ErrorType(Exception? exception)
        {
            if (exception == null)
                return "unknown";
            return exception.GetType().Name;
        }

        /// <summary>
        /// The outcome label for an exception: canceled for an <see cref="OperationCanceledException"/>,
        /// otherwise failure.
        /// </summary>
        /// <param name="exception">The exception.</param>
        /// <returns>The outcome label.</returns>
        public static string OutcomeOf(Exception exception)
        {
            return exception is OperationCanceledException ? TelemetryNames.OutcomeCanceled : TelemetryNames.OutcomeFailure;
        }

        #endregion

        #region Internal-Methods

        internal static void Add(Counter<long> counter, long value, in TagList tags)
        {
            try
            {
                counter.Add(value, tags);
            }
            catch (Exception)
            {
                // Best-effort: a failing listener never affects the caller.
            }
        }

        internal static void Add(UpDownCounter<long> counter, long value, in TagList tags)
        {
            try
            {
                counter.Add(value, tags);
            }
            catch (Exception)
            {
                // Best-effort.
            }
        }

        internal static void Record(Histogram<double> histogram, double value, in TagList tags)
        {
            try
            {
                histogram.Record(value, tags);
            }
            catch (Exception)
            {
                // Best-effort.
            }
        }

        internal static void RecordSince(Histogram<double> histogram, long startTimestamp, string stage)
        {
            if (!histogram.Enabled)
                return;
            TagList tags = new TagList();
            tags.Add(TelemetryNames.AttrStage, stage);
            Record(histogram, Stopwatch.GetElapsedTime(startTimestamp).TotalSeconds, tags);
        }

        internal static void RecordError(string component, Exception exception)
        {
            TagList tags = new TagList();
            tags.Add(TelemetryNames.AttrComponent, component);
            tags.Add(TelemetryNames.AttrErrorType, ErrorType(exception));
            Add(Errors, 1, tags);
        }

        internal static void RecordOperation(Counter<long> counter, Histogram<double> duration, string labelKey, string labelValue, string outcome, string? errorType, double seconds)
        {
            TagList tags = new TagList();
            tags.Add(labelKey, labelValue);
            tags.Add(TelemetryNames.AttrOutcome, outcome);
            Record(duration, seconds, tags);
            if (errorType != null)
                tags.Add(TelemetryNames.AttrErrorType, errorType);
            Add(counter, 1, tags);
        }

        internal static void SetTag(Activity? activity, string key, object? value)
        {
            if (activity == null)
                return;
            try
            {
                activity.SetTag(key, value);
            }
            catch (Exception)
            {
                // Best-effort.
            }
        }

        internal static void MarkSuccess(Activity? activity)
        {
            if (activity == null)
                return;
            try
            {
                activity.SetTag(TelemetryNames.AttrOutcome, TelemetryNames.OutcomeSuccess);
                activity.SetStatus(ActivityStatusCode.Ok);
            }
            catch (Exception)
            {
                // Best-effort.
            }
        }

        internal static void MarkException(Activity? activity, Exception exception)
        {
            if (activity == null || exception == null)
                return;
            try
            {
                string outcome = OutcomeOf(exception);
                activity.SetTag(TelemetryNames.AttrOutcome, outcome);
                if (outcome == TelemetryNames.OutcomeCanceled)
                {
                    // A user stop is not an error; leave the status unset so error filters skip it.
                    return;
                }

                activity.SetTag(TelemetryNames.AttrErrorType, ErrorType(exception));
                activity.SetStatus(ActivityStatusCode.Error, exception.Message);
                ActivityTagsCollection tags = new ActivityTagsCollection();
                tags.Add("exception.type", exception.GetType().FullName);
                tags.Add("exception.message", exception.Message);
                tags.Add("exception.stacktrace", exception.ToString());
                activity.AddEvent(new ActivityEvent("exception", DateTimeOffset.UtcNow, tags));
            }
            catch (Exception)
            {
                // Best-effort.
            }
        }

        internal static void Stop(Activity? activity)
        {
            if (activity == null)
                return;
            try
            {
                activity.Dispose();
            }
            catch (Exception)
            {
                // Best-effort.
            }
        }

        internal static void NoteSchedulerTick(DateTime completedUtc, long pending)
        {
            Interlocked.Exchange(ref _LastSchedulerTickUnixMs, new DateTimeOffset(DateTime.SpecifyKind(completedUtc, DateTimeKind.Utc)).ToUnixTimeMilliseconds());
            Interlocked.Exchange(ref _SchedulesPending, pending);
        }

        #endregion

        #region Private-Methods

        private static Histogram<double> Histogram(string name, string description, double[] buckets)
        {
            InstrumentAdvice<double> advice = new InstrumentAdvice<double> { HistogramBucketBoundaries = buckets };
            return _Meter.CreateHistogram<double>(name, "s", description, null, advice);
        }

        private static string ResolveVersion()
        {
            try
            {
                Assembly assembly = typeof(ArmorTelemetry).Assembly;
                AssemblyInformationalVersionAttribute? informational = assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>();
                string? version = informational?.InformationalVersion;
                if (String.IsNullOrWhiteSpace(version))
                    version = assembly.GetName().Version?.ToString();
                if (String.IsNullOrWhiteSpace(version))
                    return "0.0.0";

                // Drop source-link build metadata ("+<commit>") so the label stays one value per release.
                int plus = version!.IndexOf('+');
                return plus > 0 ? version.Substring(0, plus) : version;
            }
            catch (Exception)
            {
                return "0.0.0";
            }
        }

        private static string ResolveOsType()
        {
            if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
                return "windows";
            if (RuntimeInformation.IsOSPlatform(OSPlatform.OSX))
                return "darwin";
            if (RuntimeInformation.IsOSPlatform(OSPlatform.Linux))
                return "linux";
            return "other";
        }

        private static IEnumerable<Measurement<double>> ObserveLastBackupSuccess()
        {
            long value = Interlocked.Read(ref _LastBackupSuccessUnixMs);
            if (value <= 0)
                return Array.Empty<Measurement<double>>();
            return new Measurement<double>[] { new Measurement<double>(value / 1000.0) };
        }

        private static IEnumerable<Measurement<double>> ObserveLastSchedulerTick()
        {
            long value = Interlocked.Read(ref _LastSchedulerTickUnixMs);
            if (value <= 0)
                return Array.Empty<Measurement<double>>();
            return new Measurement<double>[] { new Measurement<double>(value / 1000.0) };
        }

        private static IEnumerable<Measurement<long>> ObserveSchedulesPending()
        {
            long value = Interlocked.Read(ref _SchedulesPending);
            if (value < 0)
                return Array.Empty<Measurement<long>>();
            return new Measurement<long>[] { new Measurement<long>(value) };
        }

        private static IEnumerable<Measurement<long>> ObserveBuildInfo()
        {
            string process;
            try
            {
                process = Assembly.GetEntryAssembly()?.GetName().Name ?? "unknown";
            }
            catch (Exception)
            {
                process = "unknown";
            }

            TagList tags = new TagList();
            tags.Add(TelemetryNames.AttrVersion, _Version);
            tags.Add(TelemetryNames.AttrProcess, process);
            tags.Add(TelemetryNames.AttrOsType, ResolveOsType());
            tags.Add(TelemetryNames.AttrRuntimeVersion, Environment.Version.ToString());
            return new Measurement<long>[] { new Measurement<long>(1, tags) };
        }

        private static IEnumerable<Measurement<double>> ObserveTickInterval()
        {
            ArmorSettings? settings = _Settings;
            if (settings == null)
                return Array.Empty<Measurement<double>>();
            return new Measurement<double>[] { new Measurement<double>(settings.SchedulerTickSeconds) };
        }

        private static IEnumerable<Measurement<long>> ObserveChunkSizes()
        {
            ArmorSettings? settings = _Settings;
            if (settings == null)
                return Array.Empty<Measurement<long>>();
            return new Measurement<long>[]
            {
                new Measurement<long>(settings.Chunking.MinSizeBytes, new KeyValuePair<string, object?>(TelemetryNames.AttrChunkBound, "min")),
                new Measurement<long>(settings.Chunking.AvgSizeBytes, new KeyValuePair<string, object?>(TelemetryNames.AttrChunkBound, "avg")),
                new Measurement<long>(settings.Chunking.MaxSizeBytes, new KeyValuePair<string, object?>(TelemetryNames.AttrChunkBound, "max")),
            };
        }

        private static IEnumerable<Measurement<long>> ObserveTraceChunkOperations()
        {
            return new Measurement<long>[] { new Measurement<long>(_TraceChunkOperations ? 1 : 0) };
        }

        #endregion
    }
}
