namespace Armor.Core.Models
{
    using System;

    /// <summary>
    /// The public header written to a storage target's repository root. It records the format version,
    /// cipher and key-derivation parameters, the wrapped data key (in both passphrase and key-file
    /// forms when available), and the chunking parameters. Because it carries the wrapped key and the
    /// parameters needed to unwrap it, a fresh machine can restore from the target plus a passphrase or
    /// key file alone. The header is stored as plaintext JSON: everything sensitive inside it is already
    /// wrapped.
    /// </summary>
    public class RepositoryHeader
    {
        private int _FormatVersion = 1;

        /// <summary>
        /// Repository format version. Default is 1. Clamped to a minimum of 1.
        /// </summary>
        public int FormatVersion
        {
            get
            {
                return _FormatVersion;
            }
            set
            {
                _FormatVersion = value < 1 ? 1 : value;
            }
        }

        /// <summary>
        /// Identifier of the encryption key this repository was created with.
        /// </summary>
        public string? EncryptionKeyId { get; set; } = null;

        /// <summary>
        /// Content cipher name (for example, <c>AES-256-GCM</c>).
        /// </summary>
        public string CipherName { get; set; } = "AES-256-GCM";

        /// <summary>
        /// Key-derivation function name (for example, <c>PBKDF2-HMAC-SHA256</c>).
        /// </summary>
        public string KdfName { get; set; } = "PBKDF2-HMAC-SHA256";

        /// <summary>
        /// Key-derivation iteration count.
        /// </summary>
        public int KdfIterations { get; set; } = 600000;

        /// <summary>
        /// Base64-encoded key-derivation salt.
        /// </summary>
        public string? KdfSaltBase64 { get; set; } = null;

        /// <summary>
        /// Whether the data key is wrapped under a passphrase-derived key.
        /// </summary>
        public bool UsesPassphrase { get; set; } = false;

        /// <summary>
        /// Whether the data key is wrapped under a key file.
        /// </summary>
        public bool UsesKeyFile { get; set; } = false;

        /// <summary>
        /// Base64-encoded data key wrapped under the passphrase-derived key, or null.
        /// </summary>
        public string? PassphraseWrappedKeyBase64 { get; set; } = null;

        /// <summary>
        /// Base64-encoded data key wrapped under the key-file key, or null.
        /// </summary>
        public string? KeyFileWrappedKeyBase64 { get; set; } = null;

        /// <summary>
        /// Minimum chunk size in bytes at repository creation.
        /// </summary>
        public int ChunkMinSizeBytes { get; set; } = 0;

        /// <summary>
        /// Average chunk size in bytes at repository creation.
        /// </summary>
        public int ChunkAvgSizeBytes { get; set; } = 0;

        /// <summary>
        /// Maximum chunk size in bytes at repository creation.
        /// </summary>
        public int ChunkMaxSizeBytes { get; set; } = 0;

        /// <summary>
        /// UTC timestamp when the repository header was created.
        /// </summary>
        public DateTime CreatedUtc { get; set; } = DateTime.UtcNow;

        /// <summary>
        /// Initializes a new instance of the <see cref="RepositoryHeader"/> class.
        /// </summary>
        public RepositoryHeader()
        {
        }

        /// <summary>
        /// Build a header from an encryption-key entry and chunking settings.
        /// </summary>
        /// <param name="key">The encryption-key entry. Cannot be null.</param>
        /// <param name="chunking">The chunking settings. Cannot be null.</param>
        /// <returns>A populated header.</returns>
        /// <exception cref="ArgumentNullException">Thrown when an argument is null.</exception>
        public static RepositoryHeader FromEncryptionKey(EncryptionKey key, Armor.Core.Configuration.ChunkingSettings chunking)
        {
            if (key == null)
                throw new ArgumentNullException(nameof(key));
            if (chunking == null)
                throw new ArgumentNullException(nameof(chunking));

            RepositoryHeader header = new RepositoryHeader();
            header.EncryptionKeyId = key.Id;
            header.CipherName = key.CipherName;
            header.KdfName = key.KdfName;
            header.KdfIterations = key.KdfIterations;
            header.KdfSaltBase64 = key.KdfSaltBase64;
            header.UsesPassphrase = key.UsesPassphrase;
            header.UsesKeyFile = key.UsesKeyFile;
            header.PassphraseWrappedKeyBase64 = key.PassphraseWrappedKeyBase64;
            header.KeyFileWrappedKeyBase64 = key.KeyFileWrappedKeyBase64;
            header.ChunkMinSizeBytes = chunking.MinSizeBytes;
            header.ChunkAvgSizeBytes = chunking.AvgSizeBytes;
            header.ChunkMaxSizeBytes = chunking.MaxSizeBytes;
            return header;
        }

        /// <summary>
        /// Reconstruct an encryption-key entry from this header, for disaster recovery from the target.
        /// </summary>
        /// <returns>An encryption-key entry carrying the header's wrapped material and parameters.</returns>
        public EncryptionKey ToEncryptionKey()
        {
            EncryptionKey key = new EncryptionKey();
            if (!String.IsNullOrWhiteSpace(EncryptionKeyId))
                key.Id = EncryptionKeyId!;
            key.Name = "Recovered key " + key.Id;
            key.CipherName = CipherName;
            key.KdfName = KdfName;
            key.KdfIterations = KdfIterations;
            key.KdfSaltBase64 = KdfSaltBase64;
            key.UsesPassphrase = UsesPassphrase;
            key.UsesKeyFile = UsesKeyFile;
            key.PassphraseWrappedKeyBase64 = PassphraseWrappedKeyBase64;
            key.KeyFileWrappedKeyBase64 = KeyFileWrappedKeyBase64;
            return key;
        }
    }
}
