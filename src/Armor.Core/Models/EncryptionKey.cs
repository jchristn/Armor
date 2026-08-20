namespace Armor.Core.Models
{
    using System;
    using Armor.Core.Helpers;

    /// <summary>
    /// An encryption-key entry. It holds the wrapped repository data key together with the
    /// parameters needed to unwrap it. The same data key may be wrapped under a passphrase-derived
    /// key, a key file, or both; either wrapping can restore the repository. Raw key material is
    /// never stored here — only wrapped forms and public parameters.
    /// </summary>
    public class EncryptionKey
    {
        private string _Id = IdGenerator.GenerateEncryptionKeyId();
        private string _Name = String.Empty;
        private string _CipherName = "AES-256-GCM";
        private string _KdfName = "PBKDF2-HMAC-SHA256";
        private int _KdfIterations = 600000;

        /// <summary>
        /// Unique, K-sortable key identifier prefixed with <see cref="Constants.EncryptionKeyIdPrefix"/>.
        /// Defaults to a freshly generated identifier. Cannot be null or whitespace.
        /// </summary>
        /// <exception cref="ArgumentNullException">Thrown when set to null or whitespace.</exception>
        public string Id
        {
            get
            {
                return _Id;
            }
            set
            {
                if (String.IsNullOrWhiteSpace(value))
                    throw new ArgumentNullException(nameof(Id));
                _Id = value;
            }
        }

        /// <summary>
        /// Human-readable key name. Cannot be null or whitespace.
        /// </summary>
        /// <exception cref="ArgumentNullException">Thrown when set to null or whitespace.</exception>
        public string Name
        {
            get
            {
                return _Name;
            }
            set
            {
                if (String.IsNullOrWhiteSpace(value))
                    throw new ArgumentNullException(nameof(Name));
                _Name = value;
            }
        }

        /// <summary>
        /// Name of the content cipher. Default is <c>AES-256-GCM</c>. Cannot be null or whitespace.
        /// </summary>
        /// <exception cref="ArgumentNullException">Thrown when set to null or whitespace.</exception>
        public string CipherName
        {
            get
            {
                return _CipherName;
            }
            set
            {
                if (String.IsNullOrWhiteSpace(value))
                    throw new ArgumentNullException(nameof(CipherName));
                _CipherName = value;
            }
        }

        /// <summary>
        /// Name of the key-derivation function used for passphrase wrapping. Default is
        /// <c>PBKDF2-HMAC-SHA256</c>. Cannot be null or whitespace.
        /// </summary>
        /// <exception cref="ArgumentNullException">Thrown when set to null or whitespace.</exception>
        public string KdfName
        {
            get
            {
                return _KdfName;
            }
            set
            {
                if (String.IsNullOrWhiteSpace(value))
                    throw new ArgumentNullException(nameof(KdfName));
                _KdfName = value;
            }
        }

        /// <summary>
        /// Iteration count for the key-derivation function. Default is 600000. Clamped to the range
        /// 100000 to 10000000. Higher values increase brute-force cost and per-unlock latency.
        /// </summary>
        public int KdfIterations
        {
            get
            {
                return _KdfIterations;
            }
            set
            {
                _KdfIterations = Math.Clamp(value, 100000, 10000000);
            }
        }

        /// <summary>
        /// Base64-encoded salt fed to the key-derivation function. Null until the key is provisioned.
        /// </summary>
        public string? KdfSaltBase64 { get; set; } = null;

        /// <summary>
        /// Whether the data key is wrapped under a passphrase-derived key. Default is false.
        /// </summary>
        public bool UsesPassphrase { get; set; } = false;

        /// <summary>
        /// Whether the data key is wrapped under a key file. Default is false.
        /// </summary>
        public bool UsesKeyFile { get; set; } = false;

        /// <summary>
        /// Base64-encoded data key wrapped under the passphrase-derived key. Null when
        /// <see cref="UsesPassphrase"/> is false.
        /// </summary>
        public string? PassphraseWrappedKeyBase64 { get; set; } = null;

        /// <summary>
        /// Base64-encoded data key wrapped under the key-file key. Null when <see cref="UsesKeyFile"/>
        /// is false.
        /// </summary>
        public string? KeyFileWrappedKeyBase64 { get; set; } = null;

        /// <summary>
        /// UTC timestamp when the key was created. Default is the current UTC time.
        /// </summary>
        public DateTime CreatedUtc { get; set; } = DateTime.UtcNow;

        /// <summary>
        /// Initializes a new instance of the <see cref="EncryptionKey"/> class.
        /// </summary>
        public EncryptionKey()
        {
        }
    }
}
