namespace Armor.Core.Service
{
    using System;
    using System.Collections.Generic;
    using System.IO;
    using System.Threading;
    using System.Threading.Tasks;
    using Armor.Core.Database;
    using Armor.Core.Enums;
    using Armor.Core.Exceptions;
    using Armor.Core.Models;
    using Armor.Core.Security;
    using Armor.Core.Storage;

    /// <summary>
    /// Manages storage targets with credential protection. Secret fields (passwords and keys) are
    /// encrypted at rest with the credential protector before persistence and decrypted when a target
    /// is read for use. Also builds repositories and validates connections.
    /// </summary>
    public sealed class StorageTargetService
    {
        private readonly DatabaseDriverBase _Database;
        private readonly CredentialProtector _Protector;

        /// <summary>
        /// Initializes a new instance of the <see cref="StorageTargetService"/> class.
        /// </summary>
        /// <param name="database">The database driver. Cannot be null.</param>
        /// <param name="protector">The credential protector. Cannot be null.</param>
        /// <exception cref="ArgumentNullException">Thrown when an argument is null.</exception>
        public StorageTargetService(DatabaseDriverBase database, CredentialProtector protector)
        {
            _Database = database ?? throw new ArgumentNullException(nameof(database));
            _Protector = protector ?? throw new ArgumentNullException(nameof(protector));
        }

        /// <summary>
        /// Create a storage target, protecting its secret fields at rest.
        /// </summary>
        /// <param name="target">The target to create (with plaintext secrets). Cannot be null.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>The created target (with plaintext secrets, as supplied).</returns>
        /// <exception cref="ArgumentNullException">Thrown when <paramref name="target"/> is null.</exception>
        public async Task<StorageTarget> CreateAsync(StorageTarget target, CancellationToken token = default)
        {
            if (target == null)
                throw new ArgumentNullException(nameof(target));

            await ProtectAsync(target, token).ConfigureAwait(false);
            try
            {
                await _Database.StorageTargets.CreateAsync(target, token).ConfigureAwait(false);
            }
            finally
            {
                await UnprotectAsync(target, token).ConfigureAwait(false);
            }
            return target;
        }

        /// <summary>
        /// Update a storage target, protecting its secret fields at rest.
        /// </summary>
        /// <param name="target">The target to update (with plaintext secrets). Cannot be null.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>The updated target (with plaintext secrets, as supplied).</returns>
        /// <exception cref="ArgumentNullException">Thrown when <paramref name="target"/> is null.</exception>
        public async Task<StorageTarget> UpdateAsync(StorageTarget target, CancellationToken token = default)
        {
            if (target == null)
                throw new ArgumentNullException(nameof(target));

            await ProtectAsync(target, token).ConfigureAwait(false);
            try
            {
                await _Database.StorageTargets.UpdateAsync(target, token).ConfigureAwait(false);
            }
            finally
            {
                await UnprotectAsync(target, token).ConfigureAwait(false);
            }
            return target;
        }

        /// <summary>
        /// Read a storage target and decrypt its secret fields for use.
        /// </summary>
        /// <param name="id">Target identifier. Cannot be null or whitespace.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>The target with decrypted secrets, or null if not found.</returns>
        public async Task<StorageTarget?> ReadDecryptedAsync(string id, CancellationToken token = default)
        {
            StorageTarget? target = await _Database.StorageTargets.ReadAsync(id, token).ConfigureAwait(false);
            if (target == null)
                return null;
            await UnprotectAsync(target, token).ConfigureAwait(false);
            return target;
        }

        /// <summary>
        /// Build a repository for a target by identifier, decrypting its secrets.
        /// </summary>
        /// <param name="id">Target identifier. Cannot be null or whitespace.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>A repository bound to the target.</returns>
        /// <exception cref="ArmorException">Thrown when the target does not exist.</exception>
        public async Task<IStorageRepository> BuildRepositoryAsync(string id, CancellationToken token = default)
        {
            StorageTarget? target = await ReadDecryptedAsync(id, token).ConfigureAwait(false);
            if (target == null)
                throw new ArmorException("Storage target '" + id + "' was not found.");

            // For a local/USB target, verify the volume is actually present before touching it, so we
            // never silently (re-)create the repository on the wrong disk when the drive is unplugged.
            if (target.Type == StorageTargetTypeEnum.Disk)
                EnsureDiskVolumeReady(target);

            try
            {
                return StorageRepositoryFactory.Create(target);
            }
            catch (DirectoryNotFoundException)
            {
                throw new TargetUnreachableException("Backup target '" + target.Name + "' is not reachable. If it is a removable drive, make sure it is connected.");
            }
            catch (IOException ex)
            {
                throw new TargetUnreachableException("Backup target '" + target.Name + "' is not reachable: " + ex.Message);
            }
        }

        /// <summary>
        /// Throws a clear error when a disk target's volume is not present (for example an unplugged
        /// USB drive), rather than letting a repository be created on the wrong disk.
        /// </summary>
        private static void EnsureDiskVolumeReady(StorageTarget target)
        {
            if (String.IsNullOrWhiteSpace(target.DiskPath))
                return;

            string? root;
            try
            {
                root = Path.GetPathRoot(Path.GetFullPath(target.DiskPath!));
            }
            catch (Exception)
            {
                return; // Unusual path; let the client surface any error.
            }
            if (String.IsNullOrEmpty(root))
                return;

            // On macOS and Linux a removable drive is mounted under a mount-point directory (/Volumes/USB,
            // /media/<user>/USB, ...) rather than a drive letter, so the root check below is a no-op. When the
            // drive is unplugged the volume's directory disappears; detect that before the repository tries
            // to create the path (which fails with a permission error and would be reported as a failure).
            if (!OperatingSystem.IsWindows() && IsUnderMissingMountPoint(target.DiskPath!))
                throw new TargetUnreachableException("Backup target '" + target.Name + "' is not reachable — the volume for " + target.DiskPath + " is not mounted. If it is a removable drive, make sure it is connected.");

            try
            {
                // On Windows this is a drive letter (e.g. "E:\") that reports not-ready when absent.
                // On a Unix root ("/") this is always ready, so it is a no-op there.
                DriveInfo drive = new DriveInfo(root);
                if (!drive.IsReady)
                    throw new TargetUnreachableException("Backup target '" + target.Name + "' is not reachable — the drive " + root + " is not connected or not ready.");
            }
            catch (ArgumentException)
            {
                // Not a drive-letter root (e.g. a UNC path); nothing to check here.
            }
            catch (IOException)
            {
                throw new TargetUnreachableException("Backup target '" + target.Name + "' is not reachable — the drive " + root + " is not ready.");
            }
        }

        /// <summary>
        /// Whether a Unix disk path lives on a volume that is not mounted: the path does not exist and its
        /// nearest existing ancestor is a mount-point parent (/Volumes, /media, /mnt, /run/media, or a
        /// per-user directory directly beneath /media or /run/media). A path whose nearest existing ancestor
        /// is anywhere else is an ordinary directory that is safe to create.
        /// </summary>
        /// <param name="diskPath">The target's disk path.</param>
        /// <returns>True when the path's volume appears to be unmounted.</returns>
        internal static bool IsUnderMissingMountPoint(string diskPath)
        {
            string full;
            try
            {
                full = Path.TrimEndingDirectorySeparator(Path.GetFullPath(diskPath));
            }
            catch (Exception)
            {
                return false;
            }
            if (Directory.Exists(full))
                return false;

            string? ancestor = Path.GetDirectoryName(full);
            while (!String.IsNullOrEmpty(ancestor) && !Directory.Exists(ancestor))
                ancestor = Path.GetDirectoryName(ancestor);
            if (String.IsNullOrEmpty(ancestor))
                return false;

            if (ancestor == "/Volumes" || ancestor == "/media" || ancestor == "/mnt" || ancestor == "/run/media")
                return true;
            string? parent = Path.GetDirectoryName(ancestor);
            return parent == "/media" || parent == "/run/media";
        }

        /// <summary>
        /// Permanently delete every object in a target's repository — the header, manifests, sidecars,
        /// and all content chunks — emptying it of backup data. The target row itself is not removed.
        /// </summary>
        /// <param name="id">Target identifier. Cannot be null or whitespace.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>The number of objects deleted.</returns>
        /// <exception cref="ArmorException">Thrown when the target does not exist.</exception>
        public async Task<int> PurgeAsync(string id, CancellationToken token = default)
        {
            IStorageRepository repository = await BuildRepositoryAsync(id, token).ConfigureAwait(false);

            // Collect keys first, then delete, so the enumeration is not disturbed by the deletes.
            List<string> keys = new List<string>();
            await foreach (string key in repository.EnumerateKeysAsync(String.Empty, token).ConfigureAwait(false))
                keys.Add(key);

            int deleted = 0;
            foreach (string key in keys)
            {
                await repository.DeleteObjectAsync(key, token).ConfigureAwait(false);
                deleted++;
            }
            return deleted;
        }

        /// <summary>
        /// Validate connectivity to a target by identifier.
        /// </summary>
        /// <param name="id">Target identifier. Cannot be null or whitespace.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>True if the connection round-trips; false if the probe read back different bytes.</returns>
        /// <exception cref="ArmorStorageException">Thrown when the probe cannot be written, read back, or
        /// deleted; the message names the failed step and the underlying error.</exception>
        public async Task<bool> ValidateAsync(string id, CancellationToken token = default)
        {
            IStorageRepository repository = await BuildRepositoryAsync(id, token).ConfigureAwait(false);
            return await repository.ValidateConnectionAsync(token).ConfigureAwait(false);
        }

        private async Task ProtectAsync(StorageTarget target, CancellationToken token)
        {
            target.Password = await ProtectValueAsync(target.Password, token).ConfigureAwait(false);
            target.SecretKey = await ProtectValueAsync(target.SecretKey, token).ConfigureAwait(false);
            target.AccountKey = await ProtectValueAsync(target.AccountKey, token).ConfigureAwait(false);
            target.CredentialJson = await ProtectValueAsync(target.CredentialJson, token).ConfigureAwait(false);
        }

        private async Task UnprotectAsync(StorageTarget target, CancellationToken token)
        {
            target.Password = await UnprotectValueAsync(target.Password, token).ConfigureAwait(false);
            target.SecretKey = await UnprotectValueAsync(target.SecretKey, token).ConfigureAwait(false);
            target.AccountKey = await UnprotectValueAsync(target.AccountKey, token).ConfigureAwait(false);
            target.CredentialJson = await UnprotectValueAsync(target.CredentialJson, token).ConfigureAwait(false);
        }

        private async Task<string?> ProtectValueAsync(string? value, CancellationToken token)
        {
            if (String.IsNullOrEmpty(value))
                return value;
            return await _Protector.ProtectAsync(value, token).ConfigureAwait(false);
        }

        private async Task<string?> UnprotectValueAsync(string? value, CancellationToken token)
        {
            if (String.IsNullOrEmpty(value))
                return value;
            return await _Protector.UnprotectAsync(value, token).ConfigureAwait(false);
        }
    }
}
