namespace Armor.Core.Service
{
    using System;
    using System.Collections.Generic;
    using System.Diagnostics;
    using System.Threading;
    using System.Threading.Tasks;
    using Armor.Core.Engine;
    using Armor.Core.Enums;
    using Armor.Core.Exceptions;
    using Armor.Core.Models;
    using Armor.Core.Scheduling;
    using Armor.Core.Storage;
    using Armor.Core.Telemetry;

    /// <summary>
    /// Runs a policy backup end-to-end: it resolves the policy's storage target and encryption key,
    /// takes the cross-process run lock so the policy cannot back up twice at once, runs the backup
    /// engine, and optionally applies retention. The caller supplies the unlocked data key.
    /// </summary>
    public sealed class BackupService
    {
        private readonly ArmorContext _Context;

        /// <summary>
        /// Initializes a new instance of the <see cref="BackupService"/> class.
        /// </summary>
        /// <param name="context">The runtime context. Cannot be null.</param>
        /// <exception cref="ArgumentNullException">Thrown when <paramref name="context"/> is null.</exception>
        public BackupService(ArmorContext context)
        {
            _Context = context ?? throw new ArgumentNullException(nameof(context));
        }

        /// <summary>
        /// Run a backup for a policy.
        /// </summary>
        /// <param name="policyId">Policy identifier. Cannot be null or whitespace.</param>
        /// <param name="dataKey">The unlocked 32-byte data key. Cannot be null.</param>
        /// <param name="backupTypeOverride">Optional backup type overriding the policy's configured type.</param>
        /// <param name="runRetention">Whether to apply retention after a successful backup.</param>
        /// <param name="token">Cancellation token.</param>
        /// <param name="progress">Optional observer notified as files are processed.</param>
        /// <returns>The completed backup-job record.</returns>
        /// <exception cref="ArgumentNullException">Thrown when a required argument is null.</exception>
        /// <exception cref="ArmorException">Thrown when the policy, target, or key is missing.</exception>
        /// <exception cref="TargetUnreachableException">Thrown when the storage target is not reachable (for example an unplugged removable drive).</exception>
        /// <exception cref="PolicyAlreadyRunningException">Thrown when the policy is already backing up (its run lock is held).</exception>
        public async Task<BackupJob> RunAsync(string policyId, byte[] dataKey, BackupTypeEnum? backupTypeOverride, bool runRetention, CancellationToken token = default, IProgress<BackupProgress>? progress = null)
        {
            if (String.IsNullOrWhiteSpace(policyId))
                throw new ArgumentNullException(nameof(policyId));
            if (dataKey == null)
                throw new ArgumentNullException(nameof(dataKey));

            // Root span of the whole request. The engine's backup.job span (and its stage:<name> children)
            // and the retention.run span nest under it, as does every storage call.
            Activity? activity = ArmorTelemetry.StartActivity(TelemetryNames.SpanBackupRun);
            ArmorTelemetry.SetTag(activity, TelemetryNames.AttrPolicyId, policyId);
            if (backupTypeOverride.HasValue)
                ArmorTelemetry.SetTag(activity, TelemetryNames.AttrBackupType, ArmorTelemetry.BackupTypeLabel(backupTypeOverride.Value));
            bool engineStarted = false;
            try
            {
                BackupJob job = await RunCoreAsync(policyId, dataKey, backupTypeOverride, runRetention, token, progress, activity, () => engineStarted = true).ConfigureAwait(false);
                ArmorTelemetry.MarkSuccess(activity);
                return job;
            }
            catch (Exception ex)
            {
                // A failure inside the engine is already counted by the engine's job metrics; one before it
                // (missing policy or key, unreachable target, held run lock) is counted here so every refused
                // request is visible by error type.
                if (!engineStarted && !(ex is OperationCanceledException))
                    ArmorTelemetry.RecordError(TelemetryNames.ComponentBackup, ex);
                ArmorTelemetry.MarkException(activity, ex);
                throw;
            }
            finally
            {
                ArmorTelemetry.Stop(activity);
            }
        }

        private async Task<BackupJob> RunCoreAsync(string policyId, byte[] dataKey, BackupTypeEnum? backupTypeOverride, bool runRetention, CancellationToken token, IProgress<BackupProgress>? progress, Activity? activity, Action onEngineStarted)
        {
            TelemetryStage prepareStage = TelemetryStage.Start(ArmorTelemetry.BackupStage, ArmorTelemetry.BackupStageDuration, TelemetryNames.StagePrepare);
            Policy policy;
            EncryptionKey? encryptionKey;
            IStorageRepository repository;
            try
            {
                policy = await RequirePolicyAsync(policyId, token).ConfigureAwait(false);
                if (String.IsNullOrWhiteSpace(policy.StorageTargetId))
                    throw new ArmorException("Policy '" + policyId + "' has no storage target assigned.");
                if (String.IsNullOrWhiteSpace(policy.EncryptionKeyId))
                    throw new ArmorException("Policy '" + policyId + "' has no encryption key assigned.");

                encryptionKey = await _Context.Database.EncryptionKeys.ReadAsync(policy.EncryptionKeyId!, token).ConfigureAwait(false);
                if (encryptionKey == null)
                    throw new ArmorException("Encryption key '" + policy.EncryptionKeyId + "' for policy '" + policyId + "' was not found.");

                StorageTargetService targetService = new StorageTargetService(_Context.Database, _Context.CredentialProtector);
                repository = await targetService.BuildRepositoryAsync(policy.StorageTargetId!, token).ConfigureAwait(false);
                ArmorTelemetry.SetTag(activity, TelemetryNames.AttrStorageTargetId, policy.StorageTargetId);
                if (repository is InstrumentedStorageRepository instrumented)
                    ArmorTelemetry.SetTag(activity, TelemetryNames.AttrStorageType, instrumented.StorageType);

                // If this policy has produced backups before, its target must already hold a repository. A
                // missing header means the target is not reachable (for example an unmounted drive) — fail
                // rather than initialize a fresh repository somewhere it does not belong.
                bool headerPresent = await repository.ObjectExistsAsync(RepositoryKeys.HeaderKey, token).ConfigureAwait(false);
                if (!headerPresent)
                {
                    List<BackupJob> priorJobs = await _Context.Database.BackupJobs.ReadByPolicyAsync(policy.Id, token).ConfigureAwait(false);
                    bool hadCompletedBackup = false;
                    foreach (BackupJob prior in priorJobs)
                    {
                        if (prior.Status == JobStatusEnum.Completed)
                        {
                            hadCompletedBackup = true;
                            break;
                        }
                    }
                    if (hadCompletedBackup)
                        throw new ArmorException("Backup target for policy '" + policy.Name + "' is not reachable — no existing backup repository was found where one is expected. If this is a removable drive, make sure it is connected.");
                }
                prepareStage.Succeed();
            }
            catch (Exception ex)
            {
                prepareStage.Fail(ex);
                throw;
            }
            finally
            {
                prepareStage.Dispose();
            }

            RunLock runLock = new RunLock(_Context.Paths.StateDirectory);
            RunLockHandle? handle = runLock.TryAcquire(policy.Id);
            if (handle == null)
            {
                ArmorTelemetry.Add(ArmorTelemetry.BackupLockRejections, 1, default(TagList));
                throw new PolicyAlreadyRunningException("Policy '" + policyId + "' is already running; the run lock is held.");
            }

            using (handle)
            {
                Diagnostics.ArmorLog.Info("Backup started for policy '" + policy.Name + "' (" + policy.Id + "), type " + (backupTypeOverride ?? policy.BackupType) + ".");
                try
                {
                    onEngineStarted();
                    BackupEngine engine = new BackupEngine(_Context.Database);
                    BackupJob job = await engine.RunAsync(policy, repository, policy.StorageTargetId!, encryptionKey, dataKey, _Context.Settings.Chunking, backupTypeOverride, token, progress, policy.MaxParallelism).ConfigureAwait(false);

                    if (runRetention)
                    {
                        using (TelemetryStage retentionStage = TelemetryStage.Start(ArmorTelemetry.BackupStage, ArmorTelemetry.BackupStageDuration, TelemetryNames.StageRetention))
                        {
                            try
                            {
                                RetentionManager retention = new RetentionManager(_Context.Database);
                                await retention.RunAsync(policy, repository, policy.StorageTargetId!, dataKey, DateTime.UtcNow, token).ConfigureAwait(false);
                                retentionStage.Succeed();
                            }
                            catch (Exception ex)
                            {
                                retentionStage.Fail(ex);
                                throw;
                            }
                        }
                    }

                    Diagnostics.ArmorLog.Info("Backup " + job.Status + " for policy '" + policy.Name + "': " + job.FileCount + " files, " + job.BytesTotal + " bytes, " + job.ChunksWritten + " chunks written, " + job.ChunksReused + " reused.");
                    return job;
                }
                catch (OperationCanceledException)
                {
                    Diagnostics.ArmorLog.Warn("Backup canceled for policy '" + policy.Name + "' (" + policy.Id + ").");
                    throw;
                }
                catch (Exception ex)
                {
                    Diagnostics.ArmorLog.Error("Backup failed for policy '" + policy.Name + "' (" + policy.Id + "): " + ex.Message);
                    Diagnostics.ArmorLog.Exception(ex, "BackupService", "RunAsync");
                    throw;
                }
            }
        }

        private async Task<Policy> RequirePolicyAsync(string policyId, CancellationToken token)
        {
            Policy? policy = await _Context.Database.Policies.ReadAsync(policyId, token).ConfigureAwait(false);
            if (policy == null)
                throw new ArmorException("Policy '" + policyId + "' was not found.");
            return policy;
        }
    }
}
