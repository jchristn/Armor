namespace Armor.Core.Engine
{
    using System;
    using System.Collections.Generic;
    using System.IO;
    using System.Threading;
    using System.Threading.Tasks;
    using Armor.Core.ChunkStore;
    using Armor.Core.Database;
    using Armor.Core.Enums;
    using Armor.Core.Exceptions;
    using Armor.Core.Models;
    using Armor.Core.Storage;

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

            restoreJob.Status = JobStatusEnum.Running;
            restoreJob.StartedUtc = DateTime.UtcNow;
            await _Database.RestoreJobs.CreateAsync(restoreJob, token).ConfigureAwait(false);

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

                // Stream the manifest one segment at a time and restore each in-scope file as it arrives, so a
                // restore never materializes the whole file list in memory.
                await foreach (ManifestFileEntry entry in ManifestStore.StreamAsync(repository, backupJob.ManifestKey!, backupJob.Id, dataKey, token).ConfigureAwait(false))
                {
                    token.ThrowIfCancellationRequested();
                    if (!MatchesScope(entry, restoreJob.Scope, normalizedSelector))
                        continue;
                    string destination = MapDestination(entry.Path, restoreJob.DestinationRoot);
                    await RestoreFileAsync(entry, destination, repository, dataKey, token).ConfigureAwait(false);
                    restoreJob.FilesRestored += 1;
                    restoreJob.BytesRestored += entry.SizeBytes;
                    progress?.Report(new RestoreProgress
                    {
                        FilesTotal = filesTotal,
                        BytesTotal = bytesTotal,
                        FilesDone = restoreJob.FilesRestored > int.MaxValue ? int.MaxValue : (int)restoreJob.FilesRestored,
                        BytesDone = restoreJob.BytesRestored,
                        CurrentPath = entry.Path,
                    });
                }

                restoreJob.Status = JobStatusEnum.Completed;
                restoreJob.CompletedUtc = DateTime.UtcNow;
                await _Database.RestoreJobs.UpdateAsync(restoreJob, token).ConfigureAwait(false);
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

            long verified = 0;
            await foreach (ManifestFileEntry entry in ManifestStore.StreamAsync(repository, backupJob.ManifestKey!, backupJob.Id, dataKey, token).ConfigureAwait(false))
            {
                foreach (string hash in entry.ChunkHashes)
                {
                    token.ThrowIfCancellationRequested();
                    bool exists = await repository.ChunkExistsAsync(hash, token).ConfigureAwait(false);
                    if (!exists)
                        throw new ArmorStorageException("Chunk '" + hash + "' referenced by file '" + entry.Path + "' is missing from the target.");

                    byte[] stored = await repository.ReadChunkAsync(hash, token).ConfigureAwait(false);
                    ChunkFramer.Unframe(stored, dataKey, hash);
                    verified += 1;
                }
            }

            return verified;
        }

        /// <summary>Whether a file entry falls within the requested restore scope. The selector is expected pre-normalized (forward slashes).</summary>
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
                    byte[] stored = await repository.ReadChunkAsync(hash, token).ConfigureAwait(false);
                    byte[] plaintext = ChunkFramer.Unframe(stored, dataKey, hash);
                    await output.WriteAsync(plaintext.AsMemory(0, plaintext.Length), token).ConfigureAwait(false);
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

        private static string MapDestination(string sourcePath, string? destinationRoot)
        {
            if (String.IsNullOrWhiteSpace(destinationRoot))
                return sourcePath;

            string root = Path.GetPathRoot(sourcePath) ?? String.Empty;
            string relative = root.Length > 0 && sourcePath.StartsWith(root, StringComparison.Ordinal)
                ? sourcePath.Substring(root.Length)
                : sourcePath;
            relative = relative.TrimStart('/', '\\');
            return Path.Combine(destinationRoot, relative);
        }

        private static string Normalize(string path)
        {
            return path.Replace('\\', '/');
        }
    }
}
