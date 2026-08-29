namespace Armor.Core.Service
{
    using System;
    using System.Threading;
    using System.Threading.Tasks;
    using Armor.Core.Engine;
    using Armor.Core.Exceptions;
    using Armor.Core.Models;
    using Armor.Core.Storage;

    /// <summary>
    /// Runs restores and verifications end-to-end: it resolves the backup point-in-time's policy and
    /// storage target, then drives the restore engine. The caller supplies the unlocked data key.
    /// </summary>
    public sealed class RestoreService
    {
        private readonly ArmorContext _Context;

        /// <summary>
        /// Initializes a new instance of the <see cref="RestoreService"/> class.
        /// </summary>
        /// <param name="context">The runtime context. Cannot be null.</param>
        /// <exception cref="ArgumentNullException">Thrown when <paramref name="context"/> is null.</exception>
        public RestoreService(ArmorContext context)
        {
            _Context = context ?? throw new ArgumentNullException(nameof(context));
        }

        /// <summary>
        /// Run a restore.
        /// </summary>
        /// <param name="restoreJob">The restore job describing scope and destination. Cannot be null.</param>
        /// <param name="dataKey">The unlocked 32-byte data key. Cannot be null.</param>
        /// <param name="token">Cancellation token.</param>
        /// <param name="progress">Optional observer notified as files are written.</param>
        /// <returns>The completed restore-job record.</returns>
        /// <exception cref="ArgumentNullException">Thrown when a required argument is null.</exception>
        /// <exception cref="ArmorException">Thrown when the backup job, policy, or target is missing.</exception>
        public async Task<RestoreJob> RunAsync(RestoreJob restoreJob, byte[] dataKey, CancellationToken token = default, IProgress<RestoreProgress>? progress = null)
        {
            if (restoreJob == null)
                throw new ArgumentNullException(nameof(restoreJob));
            if (dataKey == null)
                throw new ArgumentNullException(nameof(dataKey));

            BackupJob backupJob = await RequireBackupJobAsync(restoreJob.BackupJobId, token).ConfigureAwait(false);
            IStorageRepository repository = await ResolveRepositoryAsync(backupJob, token).ConfigureAwait(false);

            RestoreEngine engine = new RestoreEngine(_Context.Database);
            return await engine.RunAsync(restoreJob, backupJob, repository, dataKey, token, progress).ConfigureAwait(false);
        }

        /// <summary>
        /// Verify a backup point-in-time.
        /// </summary>
        /// <param name="backupJobId">Backup job identifier. Cannot be null or whitespace.</param>
        /// <param name="dataKey">The unlocked 32-byte data key. Cannot be null.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>The number of chunk references verified.</returns>
        /// <exception cref="ArgumentNullException">Thrown when a required argument is null.</exception>
        /// <exception cref="ArmorException">Thrown when the backup job, policy, or target is missing.</exception>
        public async Task<long> VerifyAsync(string backupJobId, byte[] dataKey, CancellationToken token = default)
        {
            if (String.IsNullOrWhiteSpace(backupJobId))
                throw new ArgumentNullException(nameof(backupJobId));
            if (dataKey == null)
                throw new ArgumentNullException(nameof(dataKey));

            BackupJob backupJob = await RequireBackupJobAsync(backupJobId, token).ConfigureAwait(false);
            IStorageRepository repository = await ResolveRepositoryAsync(backupJob, token).ConfigureAwait(false);

            RestoreEngine engine = new RestoreEngine(_Context.Database);
            return await engine.VerifyAsync(backupJob, repository, dataKey, token).ConfigureAwait(false);
        }

        private async Task<BackupJob> RequireBackupJobAsync(string backupJobId, CancellationToken token)
        {
            BackupJob? job = await _Context.Database.BackupJobs.ReadAsync(backupJobId, token).ConfigureAwait(false);
            if (job == null)
                throw new ArmorException("Backup job '" + backupJobId + "' was not found.");
            return job;
        }

        private async Task<IStorageRepository> ResolveRepositoryAsync(BackupJob backupJob, CancellationToken token)
        {
            Policy? policy = await _Context.Database.Policies.ReadAsync(backupJob.PolicyId, token).ConfigureAwait(false);
            if (policy == null)
                throw new ArmorException("Policy '" + backupJob.PolicyId + "' for backup job '" + backupJob.Id + "' was not found.");
            if (String.IsNullOrWhiteSpace(policy.StorageTargetId))
                throw new ArmorException("Policy '" + policy.Id + "' has no storage target assigned.");

            StorageTargetService targetService = new StorageTargetService(_Context.Database, _Context.CredentialProtector);
            return await targetService.BuildRepositoryAsync(policy.StorageTargetId!, token).ConfigureAwait(false);
        }
    }
}
