namespace Armor.Core.Security
{
    using System;
    using System.Security.Cryptography;
    using System.Text;
    using Armor.Core.Exceptions;
    using Armor.Core.Models;

    /// <summary>
    /// Provisions and unlocks repository data keys. A single random 256-bit data key encrypts a
    /// repository; that data key is wrapped with AES-256-GCM under a key-encryption key derived from a
    /// passphrase (PBKDF2-HMAC-SHA256) and/or a key file (SHA-256 of its contents). Either wrapping can
    /// recover the same data key. Raw data keys and key-encryption keys are never persisted; only
    /// wrapped forms and public parameters are stored on the <see cref="EncryptionKey"/> entry. This
    /// type is stateless and thread-safe.
    /// </summary>
    public class Keystore
    {
        private const string AssociatedDataPrefix = "armor-datakey:";

        /// <summary>
        /// Provision a new encryption key wrapped by a passphrase, a key file, or both. At least one
        /// protection method must be supplied.
        /// </summary>
        /// <param name="name">Human-readable key name. Cannot be null or whitespace.</param>
        /// <param name="passphrase">Passphrase, or null to skip passphrase protection.</param>
        /// <param name="keyFileBytes">Key-file bytes, or null to skip key-file protection.</param>
        /// <param name="iterations">
        /// PBKDF2 iteration count for passphrase derivation. Default is 600000. Clamped by the
        /// <see cref="EncryptionKey.KdfIterations"/> setter to the range 100000 to 10000000.
        /// </param>
        /// <returns>The provisioned key entry and its plaintext data key.</returns>
        /// <exception cref="ArgumentNullException">Thrown when <paramref name="name"/> is null or whitespace.</exception>
        /// <exception cref="ArgumentException">Thrown when neither a passphrase nor a key file is supplied.</exception>
        public ProvisionedKey Provision(string name, string? passphrase, byte[]? keyFileBytes, int iterations = 600000)
        {
            if (String.IsNullOrWhiteSpace(name))
                throw new ArgumentNullException(nameof(name));

            bool hasPassphrase = !String.IsNullOrEmpty(passphrase);
            bool hasKeyFile = keyFileBytes != null && keyFileBytes.Length > 0;
            if (!hasPassphrase && !hasKeyFile)
                throw new ArgumentException("At least one of passphrase or key file must be supplied.");

            EncryptionKey entry = new EncryptionKey();
            entry.Name = name;
            entry.KdfIterations = iterations;

            byte[] dataKey = KeyMaterial.GenerateDataKey();
            byte[] salt = Pbkdf2KeyDeriver.GenerateSalt();
            entry.KdfSaltBase64 = Convert.ToBase64String(salt);
            byte[] associatedData = BuildAssociatedData(entry.Id);

            if (hasPassphrase)
            {
                byte[] kek = Pbkdf2KeyDeriver.DeriveKey(passphrase!, salt, entry.KdfIterations);
                try
                {
                    byte[] wrapped = AesGcmCipher.Encrypt(kek, dataKey, associatedData);
                    entry.PassphraseWrappedKeyBase64 = Convert.ToBase64String(wrapped);
                    entry.UsesPassphrase = true;
                }
                finally
                {
                    CryptographicOperations.ZeroMemory(kek);
                }
            }

            if (hasKeyFile)
            {
                byte[] kek = KeyMaterial.DeriveKeyFromKeyFile(keyFileBytes!);
                try
                {
                    byte[] wrapped = AesGcmCipher.Encrypt(kek, dataKey, associatedData);
                    entry.KeyFileWrappedKeyBase64 = Convert.ToBase64String(wrapped);
                    entry.UsesKeyFile = true;
                }
                finally
                {
                    CryptographicOperations.ZeroMemory(kek);
                }
            }

            return new ProvisionedKey(entry, dataKey);
        }

        /// <summary>
        /// Unlock a key's data key using a passphrase.
        /// </summary>
        /// <param name="entry">The key entry. Cannot be null.</param>
        /// <param name="passphrase">The passphrase. Cannot be null or empty.</param>
        /// <returns>The 32-byte plaintext data key.</returns>
        /// <exception cref="ArgumentNullException">Thrown when <paramref name="entry"/> is null or <paramref name="passphrase"/> is null or empty.</exception>
        /// <exception cref="ArmorCryptoException">Thrown when the key is not passphrase-protected or the passphrase is wrong.</exception>
        public byte[] UnlockWithPassphrase(EncryptionKey entry, string passphrase)
        {
            if (entry == null)
                throw new ArgumentNullException(nameof(entry));
            if (String.IsNullOrEmpty(passphrase))
                throw new ArgumentNullException(nameof(passphrase));
            if (!entry.UsesPassphrase || String.IsNullOrEmpty(entry.PassphraseWrappedKeyBase64))
                throw new ArmorCryptoException("This key is not protected by a passphrase.");
            if (String.IsNullOrEmpty(entry.KdfSaltBase64))
                throw new ArmorCryptoException("This key is missing its key-derivation salt.");

            byte[] salt = Convert.FromBase64String(entry.KdfSaltBase64);
            byte[] wrapped = Convert.FromBase64String(entry.PassphraseWrappedKeyBase64);
            byte[] kek = Pbkdf2KeyDeriver.DeriveKey(passphrase, salt, entry.KdfIterations);
            try
            {
                return AesGcmCipher.Decrypt(kek, wrapped, BuildAssociatedData(entry.Id));
            }
            finally
            {
                CryptographicOperations.ZeroMemory(kek);
            }
        }

        /// <summary>
        /// Unlock a key's data key using a key file.
        /// </summary>
        /// <param name="entry">The key entry. Cannot be null.</param>
        /// <param name="keyFileBytes">The key-file bytes. Cannot be null or empty.</param>
        /// <returns>The 32-byte plaintext data key.</returns>
        /// <exception cref="ArgumentNullException">Thrown when <paramref name="entry"/> or <paramref name="keyFileBytes"/> is null.</exception>
        /// <exception cref="ArgumentException">Thrown when <paramref name="keyFileBytes"/> is empty.</exception>
        /// <exception cref="ArmorCryptoException">Thrown when the key is not key-file-protected or the key file is wrong.</exception>
        public byte[] UnlockWithKeyFile(EncryptionKey entry, byte[] keyFileBytes)
        {
            if (entry == null)
                throw new ArgumentNullException(nameof(entry));
            if (keyFileBytes == null)
                throw new ArgumentNullException(nameof(keyFileBytes));
            if (keyFileBytes.Length == 0)
                throw new ArgumentException("Key file cannot be empty.", nameof(keyFileBytes));
            if (!entry.UsesKeyFile || String.IsNullOrEmpty(entry.KeyFileWrappedKeyBase64))
                throw new ArmorCryptoException("This key is not protected by a key file.");

            byte[] wrapped = Convert.FromBase64String(entry.KeyFileWrappedKeyBase64);
            byte[] kek = KeyMaterial.DeriveKeyFromKeyFile(keyFileBytes);
            try
            {
                return AesGcmCipher.Decrypt(kek, wrapped, BuildAssociatedData(entry.Id));
            }
            finally
            {
                CryptographicOperations.ZeroMemory(kek);
            }
        }

        private static byte[] BuildAssociatedData(string keyId)
        {
            return Encoding.UTF8.GetBytes(AssociatedDataPrefix + keyId);
        }
    }
}
