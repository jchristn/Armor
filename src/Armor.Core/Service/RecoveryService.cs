namespace Armor.Core.Service
{
    using System;
    using System.Collections.Generic;
    using System.Text;
    using System.Threading;
    using System.Threading.Tasks;
    using Armor.Core.ChunkStore;
    using Armor.Core.Database;
    using Armor.Core.Engine;
    using Armor.Core.Enums;
    using Armor.Core.Exceptions;
    using Armor.Core.Models;
    using Armor.Core.Security;
    using Armor.Core.Serialization;
    using Armor.Core.Storage;

    /// <summary>
    /// One restorable point-in-time discovered directly on a storage target during disaster recovery.
    /// The summary is read from the run's metadata sidecar (or, for older backups, from the manifest),
    /// so it carries no local policy or job records.
    /// </summary>
    public sealed class RecoveryPoint
    {
        /// <summary>Identifier of the policy that produced this point-in-time.</summary>
        public string PolicyId { get; }

        /// <summary>Human-readable policy name, when the sidecar recorded one.</summary>
        public string? PolicyName { get; }

        /// <summary>Identifier of the backup run that produced this point-in-time.</summary>
        public string JobId { get; }

        /// <summary>Storage key of this point-in-time's manifest object.</summary>
        public string ManifestKey { get; }

        /// <summary>UTC timestamp identifying the point-in-time.</summary>
        public DateTime PointInTimeUtc { get; }

        /// <summary>Backup type of the run.</summary>
        public BackupTypeEnum BackupType { get; }

        /// <summary>Number of files captured.</summary>
        public long FileCount { get; }

        /// <summary>Total source bytes captured.</summary>
        public long TotalBytes { get; }

        internal RecoveryPoint(string policyId, string? policyName, string jobId, string manifestKey, DateTime pointInTimeUtc, BackupTypeEnum backupType, long fileCount, long totalBytes)
        {
            PolicyId = policyId;
            PolicyName = policyName;
            JobId = jobId;
            ManifestKey = manifestKey;
            PointInTimeUtc = pointInTimeUtc;
            BackupType = backupType;
            FileCount = fileCount;
            TotalBytes = totalBytes;
        }
    }

    /// <summary>
    /// An opened recovery session against one storage target: the repository plus the unlocked data key.
    /// The catalog is browsed and points restored straight from the target, so a completely fresh install
    /// can recover with only the target location and the password.
    /// </summary>
    public sealed class RecoverySession
    {
        private readonly DatabaseDriverBase _Database;
        private readonly IStorageRepository _Repository;
        private readonly byte[] _DataKey;

        internal RecoverySession(DatabaseDriverBase database, IStorageRepository repository, byte[] dataKey)
        {
            _Database = database;
            _Repository = repository;
            _DataKey = dataKey;
        }

        /// <summary>
        /// Enumerate every restorable point-in-time on the target, newest first. Each point's summary is
        /// read from its metadata sidecar when present, otherwise reconstructed from its manifest.
        /// </summary>
        /// <param name="token">Cancellation token.</param>
        /// <returns>The recovery points.</returns>
        public async Task<List<RecoveryPoint>> BrowseAsync(CancellationToken token = default)
        {
            // Group the target's manifest and sidecar objects by run.
            Dictionary<string, RunKeys> runs = new Dictionary<string, RunKeys>(StringComparer.Ordinal);

            await foreach (string key in _Repository.EnumerateKeysAsync(RepositoryKeys.ManifestsPrefix, token).ConfigureAwait(false))
            {
                if (!TryParseRunKey(key, out string policyId, out string jobId, out bool isInfo))
                    continue;

                if (!runs.TryGetValue(jobId, out RunKeys? entry) || entry == null)
                {
                    entry = new RunKeys(policyId);
                    runs[jobId] = entry;
                }

                if (isInfo)
                    entry.InfoKey = key;
                else
                    entry.ManifestKey = key;
            }

            List<RecoveryPoint> points = new List<RecoveryPoint>();
            foreach (KeyValuePair<string, RunKeys> run in runs)
            {
                string jobId = run.Key;
                RunKeys keys = run.Value;
                if (keys.ManifestKey == null)
                    continue; // A sidecar with no manifest cannot be restored; skip it.

                RecoveryPoint? point = null;

                if (keys.InfoKey != null)
                {
                    try
                    {
                        byte[] infoBytes = await _Repository.ReadObjectAsync(keys.InfoKey, token).ConfigureAwait(false);
                        BackupRunInfo info = RunInfoCodec.Decode(infoBytes, _DataKey, jobId);
                        point = new RecoveryPoint(info.PolicyId, info.PolicyName, info.JobId, keys.ManifestKey, info.PointInTimeUtc, info.BackupType, info.FileCount, info.TotalBytes);
                    }
                    catch (ArmorCryptoException)
                    {
                        // Fall back to the manifest when the sidecar is unreadable.
                        point = null;
                    }
                }

                if (point == null)
                {
                    ManifestHeader header = await ManifestStore.ReadHeaderAsync(_Repository, keys.ManifestKey, jobId, _DataKey, token).ConfigureAwait(false);
                    point = new RecoveryPoint(keys.PolicyId, null, jobId, keys.ManifestKey, header.PointInTimeUtc, header.BackupType, header.FileCount, header.TotalBytes);
                }

                points.Add(point);
            }

            points.Sort((a, b) => b.PointInTimeUtc.CompareTo(a.PointInTimeUtc));
            return points;
        }

        /// <summary>
        /// The distinct folder paths contained in a point-in-time, sorted, for a folder-scoped restore.
        /// </summary>
        /// <param name="point">The point-in-time. Cannot be null.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>The folder paths.</returns>
        /// <exception cref="ArgumentNullException">Thrown when <paramref name="point"/> is null.</exception>
        public async Task<List<string>> ListFoldersAsync(RecoveryPoint point, CancellationToken token = default)
        {
            if (point == null)
                throw new ArgumentNullException(nameof(point));

            SortedSet<string> folders = new SortedSet<string>(StringComparer.OrdinalIgnoreCase);
            await foreach (ManifestFileEntry entry in ManifestStore.StreamAsync(_Repository, point.ManifestKey, point.JobId, _DataKey, token).ConfigureAwait(false))
            {
                string normalized = entry.Path.Replace('\\', '/');
                int slash = normalized.LastIndexOf('/');
                if (slash > 0)
                    folders.Add(normalized.Substring(0, slash));
            }
            return new List<string>(folders);
        }

        /// <summary>
        /// The absolute file paths contained in a point-in-time, sorted, for a file-scoped restore.
        /// </summary>
        /// <param name="point">The point-in-time. Cannot be null.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>The file paths.</returns>
        /// <exception cref="ArgumentNullException">Thrown when <paramref name="point"/> is null.</exception>
        public async Task<List<string>> ListFilesAsync(RecoveryPoint point, CancellationToken token = default)
        {
            if (point == null)
                throw new ArgumentNullException(nameof(point));

            List<string> paths = new List<string>();
            await foreach (ManifestFileEntry entry in ManifestStore.StreamAsync(_Repository, point.ManifestKey, point.JobId, _DataKey, token).ConfigureAwait(false))
                paths.Add(entry.Path);
            paths.Sort(StringComparer.OrdinalIgnoreCase);
            return paths;
        }

        /// <summary>
        /// Restore a point-in-time from the target. The restore job carries the scope, selector, and
        /// destination; the point supplies the manifest to read. No local backup-job record is required.
        /// </summary>
        /// <param name="point">The point-in-time to restore. Cannot be null.</param>
        /// <param name="restoreJob">The restore job describing scope and destination. Cannot be null.</param>
        /// <param name="token">Cancellation token.</param>
        /// <param name="progress">Optional observer notified as files are written.</param>
        /// <returns>The completed restore-job record.</returns>
        /// <exception cref="ArgumentNullException">Thrown when a required argument is null.</exception>
        public Task<RestoreJob> RestoreAsync(RecoveryPoint point, RestoreJob restoreJob, CancellationToken token = default, IProgress<RestoreProgress>? progress = null)
        {
            if (point == null)
                throw new ArgumentNullException(nameof(point));
            if (restoreJob == null)
                throw new ArgumentNullException(nameof(restoreJob));

            BackupJob synthetic = new BackupJob();
            synthetic.Id = point.JobId;
            synthetic.PolicyId = point.PolicyId;
            synthetic.ManifestKey = point.ManifestKey;
            // Carry the point's totals onto the synthetic job so a progress observer gets real denominators
            // (the engine reads FileCount/BytesTotal from this record).
            synthetic.FileCount = point.FileCount;
            synthetic.BytesTotal = point.TotalBytes;
            restoreJob.BackupJobId = point.JobId;

            RestoreEngine engine = new RestoreEngine(_Database);
            return engine.RunAsync(restoreJob, synthetic, _Repository, _DataKey, token, progress);
        }


        private static bool TryParseRunKey(string key, out string policyId, out string jobId, out bool isInfo)
        {
            // Keys look like: manifests/<policyId>/<jobId>.manifest or manifests/<policyId>/<jobId>.info
            policyId = String.Empty;
            jobId = String.Empty;
            isInfo = false;

            if (!key.StartsWith(RepositoryKeys.ManifestsPrefix, StringComparison.Ordinal))
                return false;

            string remainder = key.Substring(RepositoryKeys.ManifestsPrefix.Length);
            string[] parts = remainder.Split('/');
            if (parts.Length != 2)
                return false;

            policyId = parts[0];
            string file = parts[1];

            if (file.EndsWith(RepositoryKeys.ManifestExtension, StringComparison.Ordinal))
            {
                jobId = file.Substring(0, file.Length - RepositoryKeys.ManifestExtension.Length);
                isInfo = false;
                return jobId.Length > 0;
            }

            if (file.EndsWith(RepositoryKeys.InfoExtension, StringComparison.Ordinal))
            {
                jobId = file.Substring(0, file.Length - RepositoryKeys.InfoExtension.Length);
                isInfo = true;
                return jobId.Length > 0;
            }

            return false;
        }

        private sealed class RunKeys
        {
            public RunKeys(string policyId)
            {
                PolicyId = policyId;
            }

            public string PolicyId { get; }

            public string? ManifestKey { get; set; }

            public string? InfoKey { get; set; }
        }
    }

    /// <summary>
    /// Opens disaster-recovery sessions against a storage target using only the target location and the
    /// backup password. It reads the repository header written on every backup — which carries the
    /// password-wrapped data key and its derivation parameters — reconstructs the key, and unlocks it
    /// with the supplied password. No local policy, key, or job records are needed, so recovery works on
    /// a completely fresh install.
    /// </summary>
    public sealed class RecoveryService
    {
        private readonly ArmorContext _Context;

        /// <summary>
        /// Initializes a new instance of the <see cref="RecoveryService"/> class.
        /// </summary>
        /// <param name="context">The runtime context. Cannot be null.</param>
        /// <exception cref="ArgumentNullException">Thrown when <paramref name="context"/> is null.</exception>
        public RecoveryService(ArmorContext context)
        {
            _Context = context ?? throw new ArgumentNullException(nameof(context));
        }

        /// <summary>
        /// Open a recovery session against a storage target by unlocking its repository with a password.
        /// </summary>
        /// <param name="targetId">Identifier of the storage target to recover from. Cannot be null or whitespace.</param>
        /// <param name="password">The backup password. Cannot be null or empty.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>An opened recovery session.</returns>
        /// <exception cref="ArgumentNullException">Thrown when <paramref name="targetId"/> is null or whitespace.</exception>
        /// <exception cref="ArgumentException">Thrown when <paramref name="password"/> is null or empty.</exception>
        /// <exception cref="ArmorException">Thrown when the target holds no Armor repository or it is not password-protected.</exception>
        /// <exception cref="ArmorCryptoException">Thrown when the password does not unlock the repository.</exception>
        public async Task<RecoverySession> OpenAsync(string targetId, string password, CancellationToken token = default)
        {
            if (String.IsNullOrWhiteSpace(targetId))
                throw new ArgumentNullException(nameof(targetId));
            if (String.IsNullOrEmpty(password))
                throw new ArgumentException("A password is required.", nameof(password));

            StorageTargetService targetService = new StorageTargetService(_Context.Database, _Context.CredentialProtector);
            IStorageRepository repository = await targetService.BuildRepositoryAsync(targetId, token).ConfigureAwait(false);

            if (!await repository.ObjectExistsAsync(RepositoryKeys.HeaderKey, token).ConfigureAwait(false))
                throw new ArmorException("No Armor backup was found at this location.");

            byte[] headerBytes = await repository.ReadObjectAsync(RepositoryKeys.HeaderKey, token).ConfigureAwait(false);
            RepositoryHeader? header = ArmorJson.Deserialize<RepositoryHeader>(Encoding.UTF8.GetString(headerBytes));
            if (header == null)
                throw new ArmorException("The backup header at this location could not be read.");

            EncryptionKey key = header.ToEncryptionKey();
            if (!key.UsesPassphrase)
                throw new ArmorException("This backup is not password-protected, so it cannot be recovered by password.");

            byte[] dataKey = new Keystore().UnlockWithPassphrase(key, password);
            return new RecoverySession(_Context.Database, repository, dataKey);
        }
    }
}
