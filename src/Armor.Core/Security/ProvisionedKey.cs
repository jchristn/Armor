namespace Armor.Core.Security
{
    using System;
    using Armor.Core.Models;

    /// <summary>
    /// The result of provisioning an encryption key: the persistable <see cref="EncryptionKey"/> entry
    /// (containing only wrapped material and public parameters) together with the plaintext data key
    /// produced during provisioning, so the caller can begin encrypting immediately without a second
    /// unlock. Treat <see cref="DataKey"/> as sensitive and do not persist it.
    /// </summary>
    public class ProvisionedKey
    {
        /// <summary>
        /// The persistable key entry. Never null.
        /// </summary>
        public EncryptionKey Key { get; }

        /// <summary>
        /// The 32-byte plaintext data key. Never null. Sensitive; do not persist.
        /// </summary>
        public byte[] DataKey { get; }

        /// <summary>
        /// Initializes a new instance of the <see cref="ProvisionedKey"/> class.
        /// </summary>
        /// <param name="key">The key entry. Cannot be null.</param>
        /// <param name="dataKey">The plaintext data key. Cannot be null.</param>
        /// <exception cref="ArgumentNullException">Thrown when an argument is null.</exception>
        public ProvisionedKey(EncryptionKey key, byte[] dataKey)
        {
            Key = key ?? throw new ArgumentNullException(nameof(key));
            DataKey = dataKey ?? throw new ArgumentNullException(nameof(dataKey));
        }
    }
}
