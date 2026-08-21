namespace Armor.Core.Service
{
    using System;
    using System.IO;
    using System.Threading;
    using System.Threading.Tasks;
    using Armor.Core.Configuration;
    using Armor.Core.Database;
    using Armor.Core.Exceptions;
    using Armor.Core.Models;
    using Armor.Core.Security;

    /// <summary>
    /// Provisions and unlocks encryption keys, persisting the wrapped key material to the local
    /// keystore. Unlocking returns the plaintext data key for the caller to use with the engines; the
    /// data key is never persisted.
    /// </summary>
    public sealed class EncryptionKeyService
    {
        private readonly DatabaseDriverBase _Database;
        private readonly Keystore _Keystore = new Keystore();

        /// <summary>
        /// Initializes a new instance of the <see cref="EncryptionKeyService"/> class.
        /// </summary>
        /// <param name="database">The database driver. Cannot be null.</param>
        /// <exception cref="ArgumentNullException">Thrown when <paramref name="database"/> is null.</exception>
        public EncryptionKeyService(DatabaseDriverBase database)
        {
            _Database = database ?? throw new ArgumentNullException(nameof(database));
        }

        /// <summary>
        /// Provision and persist a new encryption key.
        /// </summary>
        /// <param name="name">Key name. Cannot be null or whitespace.</param>
        /// <param name="passphrase">Passphrase, or null to skip passphrase protection.</param>
        /// <param name="keyFileBytes">Key-file bytes, or null to skip key-file protection.</param>
        /// <param name="iterations">PBKDF2 iteration count.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>The provisioned key and its plaintext data key.</returns>
        public async Task<ProvisionedKey> ProvisionAsync(string name, string? passphrase, byte[]? keyFileBytes, int iterations, CancellationToken token = default)
        {
            ProvisionedKey provisioned = _Keystore.Provision(name, passphrase, keyFileBytes, iterations);
            await _Database.EncryptionKeys.CreateAsync(provisioned.Key, token).ConfigureAwait(false);
            return provisioned;
        }

        /// <summary>
        /// Provision a new encryption key protected by a user-chosen password, and cache that password
        /// on the local file system (encrypted at rest with the machine-local credential protector) so
        /// backups run unattended without prompting. Because every backup writes the password-wrapped
        /// data key and its salt into the repository header at the storage target, the password is the
        /// only thing needed to recover on a completely fresh machine: install Armor, point it at the
        /// target, and enter the password. There is no key file that can be lost.
        /// </summary>
        /// <param name="name">Key name. Cannot be null or whitespace.</param>
        /// <param name="password">The user-chosen password. Cannot be null or empty.</param>
        /// <param name="paths">Path resolver used to place the cached password. Cannot be null.</param>
        /// <param name="protector">Machine-local protector used to encrypt the cached password. Cannot be null.</param>
        /// <param name="iterations">PBKDF2 iteration count.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>The provisioned key and its plaintext data key.</returns>
        /// <exception cref="ArgumentNullException">Thrown when <paramref name="paths"/> or <paramref name="protector"/> is null.</exception>
        /// <exception cref="ArgumentException">Thrown when <paramref name="password"/> is null or empty.</exception>
        public async Task<ProvisionedKey> ProvisionWithPasswordAsync(string name, string password, ArmorPaths paths, CredentialProtector protector, int iterations = 600000, CancellationToken token = default)
        {
            if (paths == null)
                throw new ArgumentNullException(nameof(paths));
            if (protector == null)
                throw new ArgumentNullException(nameof(protector));
            if (String.IsNullOrEmpty(password))
                throw new ArgumentException("A password is required.", nameof(password));

            ProvisionedKey provisioned = _Keystore.Provision(name, password, null, iterations);
            await _Database.EncryptionKeys.CreateAsync(provisioned.Key, token).ConfigureAwait(false);

            // If caching the password fails, roll back the entry so we never leave a key that exists
            // in the database but cannot be unlocked unattended.
            try
            {
                string protectedPassword = await protector.ProtectAsync(password, token).ConfigureAwait(false);
                await WriteSecretFileAsync(paths.PasswordFilePath(provisioned.Key.Id), protectedPassword, token).ConfigureAwait(false);
            }
            catch
            {
                await _Database.EncryptionKeys.DeleteAsync(provisioned.Key.Id, token).ConfigureAwait(false);
                throw;
            }

            return provisioned;
        }

        /// <summary>
        /// Unlock a key's data key without human interaction, using the locally cached password (or, for
        /// keys created under the older key-file model, the stored key file). This is the path used by
        /// both manual and scheduled backups.
        /// </summary>
        /// <param name="keyId">Key identifier. Cannot be null or whitespace.</param>
        /// <param name="paths">Path resolver used to locate the cached secret. Cannot be null.</param>
        /// <param name="protector">Machine-local protector used to decrypt the cached password. Cannot be null.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>The plaintext data key.</returns>
        /// <exception cref="ArgumentNullException">Thrown when <paramref name="paths"/> or <paramref name="protector"/> is null.</exception>
        /// <exception cref="ArmorException">Thrown when the key has no cached secret on this machine.</exception>
        public async Task<byte[]> UnlockUnattendedAsync(string keyId, ArmorPaths paths, CredentialProtector protector, CancellationToken token = default)
        {
            if (paths == null)
                throw new ArgumentNullException(nameof(paths));
            if (protector == null)
                throw new ArgumentNullException(nameof(protector));

            EncryptionKey entry = await RequireKeyAsync(keyId, token).ConfigureAwait(false);

            string passwordPath = paths.PasswordFilePath(keyId);
            if (entry.UsesPassphrase && File.Exists(passwordPath))
            {
                string protectedPassword = await File.ReadAllTextAsync(passwordPath, token).ConfigureAwait(false);
                string password = await protector.UnprotectAsync(protectedPassword, token).ConfigureAwait(false);
                return _Keystore.UnlockWithPassphrase(entry, password);
            }

            // Legacy: keys created under the earlier key-file model.
            string keyFilePath = paths.KeyFilePath(keyId);
            if (entry.UsesKeyFile && File.Exists(keyFilePath))
            {
                byte[] keyFileBytes = await File.ReadAllBytesAsync(keyFilePath, token).ConfigureAwait(false);
                return _Keystore.UnlockWithKeyFile(entry, keyFileBytes);
            }

            throw new ArmorException("Key '" + keyId + "' has no cached secret on this machine; unlock it with its password.");
        }

        /// <summary>
        /// Whether the supplied key can be unlocked without human interaction — its password (or, for
        /// legacy keys, its key file) is cached on this machine.
        /// </summary>
        /// <param name="entry">The key entry. Cannot be null.</param>
        /// <param name="paths">Path resolver used to locate the cached secret. Cannot be null.</param>
        /// <returns>True when the key can be unlocked unattended.</returns>
        /// <exception cref="ArgumentNullException">Thrown when <paramref name="entry"/> or <paramref name="paths"/> is null.</exception>
        public bool CanUnlockUnattended(EncryptionKey entry, ArmorPaths paths)
        {
            if (entry == null)
                throw new ArgumentNullException(nameof(entry));
            if (paths == null)
                throw new ArgumentNullException(nameof(paths));

            if (entry.UsesPassphrase && File.Exists(paths.PasswordFilePath(entry.Id)))
                return true;
            if (entry.UsesKeyFile && File.Exists(paths.KeyFilePath(entry.Id)))
                return true;
            return false;
        }

        /// <summary>
        /// Unlock a key's data key using a passphrase.
        /// </summary>
        /// <param name="keyId">Key identifier. Cannot be null or whitespace.</param>
        /// <param name="passphrase">The passphrase. Cannot be null or empty.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>The plaintext data key.</returns>
        /// <exception cref="ArmorException">Thrown when the key does not exist.</exception>
        public async Task<byte[]> UnlockWithPassphraseAsync(string keyId, string passphrase, CancellationToken token = default)
        {
            EncryptionKey entry = await RequireKeyAsync(keyId, token).ConfigureAwait(false);
            return _Keystore.UnlockWithPassphrase(entry, passphrase);
        }

        /// <summary>
        /// Unlock a key's data key using a key file.
        /// </summary>
        /// <param name="keyId">Key identifier. Cannot be null or whitespace.</param>
        /// <param name="keyFileBytes">The key-file bytes. Cannot be null or empty.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>The plaintext data key.</returns>
        /// <exception cref="ArmorException">Thrown when the key does not exist.</exception>
        public async Task<byte[]> UnlockWithKeyFileAsync(string keyId, byte[] keyFileBytes, CancellationToken token = default)
        {
            EncryptionKey entry = await RequireKeyAsync(keyId, token).ConfigureAwait(false);
            return _Keystore.UnlockWithKeyFile(entry, keyFileBytes);
        }

        private static async Task WriteSecretFileAsync(string path, string content, CancellationToken token)
        {
            string? directory = Path.GetDirectoryName(Path.GetFullPath(path));
            if (!String.IsNullOrEmpty(directory))
                Directory.CreateDirectory(directory);

            await File.WriteAllTextAsync(path, content, token).ConfigureAwait(false);

            if (!OperatingSystem.IsWindows())
                File.SetUnixFileMode(path, UnixFileMode.UserRead | UnixFileMode.UserWrite);
        }

        private async Task<EncryptionKey> RequireKeyAsync(string keyId, CancellationToken token)
        {
            if (String.IsNullOrWhiteSpace(keyId))
                throw new ArgumentNullException(nameof(keyId));
            EncryptionKey? entry = await _Database.EncryptionKeys.ReadAsync(keyId, token).ConfigureAwait(false);
            if (entry == null)
                throw new ArmorException("Encryption key '" + keyId + "' was not found.");
            return entry;
        }
    }
}
