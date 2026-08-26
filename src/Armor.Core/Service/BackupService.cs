namespace Armor.Core.Service
{
    using System;
    using System.Collections.Generic;
    using System.Threading;
    using System.Threading.Tasks;
    using Armor.Core.Engine;
    using Armor.Core.Enums;
    using Armor.Core.Exceptions;
    using Armor.Core.Models;
    using Armor.Core.Scheduling;
    using Armor.Core.Storage;

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
        /// <exception cref="ArmorException">Thrown when the policy, target, or key is missing, or the policy is already running.</exception>
        public async Task<BackupJob> RunAsync(string policyId, byte[] dataKey, BackupTypeEnum? backupTypeOverride, bool runRetention, CancellationToken token = default, IProgress<BackupProgress>? progress = null)
        {
            if (String.IsNullOrWhiteSpace(policyId))
                throw new ArgumentNullException(nameof(policyId));
            if (dataKey == null)
                throw new ArgumentNullException(nameof(dataKey));

            Policy policy = await RequirePolicyAsync(policyId, token).ConfigureAwait(false);
            if (String.IsNullOrWhiteSpace(policy.StorageTargetId))
                throw new ArmorException("Policy '" + policyId + "' has no storage target assigned.");
            if (String.IsNullOrWhiteSpace(policy.EncryptionKeyId))
                throw new ArmorException("Policy '" + policyId + "' has no encryption key assigned.");

            EncryptionKey? encryptionKey = await _Context.Database.EncryptionKeys.ReadAsync(policy.EncryptionKeyId!, token).ConfigureAwait(false);
            if (encryptionKey == null)
                throw new ArmorException("Encryption key '" + policy.EncryptionKeyId + "' for policy '" + policyId + "' was not found.");

            StorageTargetService targetService = new StorageTargetService(_Context.Database, _Context.CredentialProtector);
            IStorageRepository repository = await targetService.BuildRepositoryAsync(policy.StorageTargetId!, token).ConfigureAwait(false);

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

            RunLock runLock = new RunLock(_Context.Paths.StateDirectory);
            RunLockHandle? handle = runLock.TryAcquire(policy.Id);
            if (handle == null)
                throw new ArmorException("Policy '" + policyId + "' is already running; the run lock is held.");

            using (handle)
            {
                Diagnostics.ArmorLog.Info("Backup started for policy '" + policy.Name + "' (" + policy.Id + "), type " + (backupTypeOverride ?? policy.BackupType) + ".");
                try
                {
                    BackupEngine engine = new BackupEngine(_Context.Database);
                    BackupJob job = await engine.RunAsync(policy, repository, policy.StorageTargetId!, encryptionKey, dataKey, _Context.Settings.Chunking, backupTypeOverride, token, progress, policy.MaxParallelism).ConfigureAwait(false);

                    if (runRetention)
                    {
                        RetentionManager retention = new RetentionManager(_Context.Database);
                        await retention.RunAsync(policy, repository, policy.StorageTargetId!, dataKey, DateTime.UtcNow, token).ConfigureAwait(false);
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
