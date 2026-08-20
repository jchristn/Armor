namespace Armor.Core.Service
{
    using System;
    using System.Threading;
    using System.Threading.Tasks;
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
