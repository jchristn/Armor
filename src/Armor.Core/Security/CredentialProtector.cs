namespace Armor.Core.Security
{
    using System;
    using System.IO;
    using System.Text;
    using System.Threading;
    using System.Threading.Tasks;

    /// <summary>
    /// Encrypts and decrypts storage-target secrets at rest using a machine-local data-protection key.
    /// The key is a random 256-bit value stored in a file with owner-only permissions; it is created
    /// on first use. Protected values are AES-256-GCM frames encoded as base64. This is a local
    /// at-rest measure, not a substitute for the repository encryption key. Instances cache the key
    /// after first load and are safe for concurrent use once loaded.
    /// </summary>
    public class CredentialProtector
    {
        private const string AssociatedData = "armor-credential";

        private readonly string _KeyFilePath;
        private readonly SemaphoreSlim _KeyLock = new SemaphoreSlim(1, 1);
        private byte[]? _Key;

        /// <summary>
        /// Absolute path to the local data-protection key file.
        /// </summary>
        public string KeyFilePath
        {
            get { return _KeyFilePath; }
        }

        /// <summary>
        /// Initializes a new instance of the <see cref="CredentialProtector"/> class.
        /// </summary>
        /// <param name="keyFilePath">Path to the local data-protection key file. Cannot be null or whitespace.</param>
        /// <exception cref="ArgumentNullException">Thrown when <paramref name="keyFilePath"/> is null or whitespace.</exception>
        public CredentialProtector(string keyFilePath)
        {
            if (String.IsNullOrWhiteSpace(keyFilePath))
                throw new ArgumentNullException(nameof(keyFilePath));
            _KeyFilePath = keyFilePath;
        }

        /// <summary>
        /// Encrypt a secret string, returning a base64-encoded protected value.
        /// </summary>
        /// <param name="plaintext">The secret. Cannot be null.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>A base64-encoded protected value.</returns>
        /// <exception cref="ArgumentNullException">Thrown when <paramref name="plaintext"/> is null.</exception>
        public async Task<string> ProtectAsync(string plaintext, CancellationToken token = default)
        {
            if (plaintext == null)
                throw new ArgumentNullException(nameof(plaintext));

            byte[] key = await EnsureKeyAsync(token).ConfigureAwait(false);
            byte[] frame = AesGcmCipher.Encrypt(key, Encoding.UTF8.GetBytes(plaintext), Encoding.UTF8.GetBytes(AssociatedData));
            return Convert.ToBase64String(frame);
        }

        /// <summary>
        /// Decrypt a base64-encoded protected value back to its plaintext secret.
        /// </summary>
        /// <param name="protectedValue">The protected value produced by <see cref="ProtectAsync"/>. Cannot be null or whitespace.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>The plaintext secret.</returns>
        /// <exception cref="ArgumentNullException">Thrown when <paramref name="protectedValue"/> is null or whitespace.</exception>
        /// <exception cref="Armor.Core.Exceptions.ArmorCryptoException">Thrown when the value is malformed or fails authentication.</exception>
        public async Task<string> UnprotectAsync(string protectedValue, CancellationToken token = default)
        {
            if (String.IsNullOrWhiteSpace(protectedValue))
                throw new ArgumentNullException(nameof(protectedValue));

            byte[] key = await EnsureKeyAsync(token).ConfigureAwait(false);
            byte[] frame = Convert.FromBase64String(protectedValue);
            byte[] plaintext = AesGcmCipher.Decrypt(key, frame, Encoding.UTF8.GetBytes(AssociatedData));
            return Encoding.UTF8.GetString(plaintext);
        }

        private async Task<byte[]> EnsureKeyAsync(CancellationToken token)
        {
            if (_Key != null)
                return _Key;

            await _KeyLock.WaitAsync(token).ConfigureAwait(false);
            try
            {
                if (_Key != null)
                    return _Key;

                if (File.Exists(_KeyFilePath))
                {
                    _Key = await ReadAllBytesAsync(_KeyFilePath, token).ConfigureAwait(false);
                    return _Key;
                }

                string? directory = Path.GetDirectoryName(Path.GetFullPath(_KeyFilePath));
                if (!String.IsNullOrEmpty(directory))
                    Directory.CreateDirectory(directory);

                byte[] generated = KeyMaterial.GenerateKeyFileBytes();
                await WriteAllBytesAsync(_KeyFilePath, generated, token).ConfigureAwait(false);
                RestrictPermissions(_KeyFilePath);
                _Key = generated;
                return _Key;
            }
            finally
            {
                _KeyLock.Release();
            }
        }

        private static async Task<byte[]> ReadAllBytesAsync(string path, CancellationToken token)
        {
            using (FileStream stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read))
            {
                byte[] buffer = new byte[stream.Length];
                int read = 0;
                while (read < buffer.Length)
                {
                    int n = await stream.ReadAsync(buffer.AsMemory(read, buffer.Length - read), token).ConfigureAwait(false);
                    if (n == 0)
                        break;
                    read += n;
                }
                return buffer;
            }
        }

        private static async Task WriteAllBytesAsync(string path, byte[] data, CancellationToken token)
        {
            using (FileStream stream = new FileStream(path, FileMode.CreateNew, FileAccess.Write, FileShare.None))
            {
                await stream.WriteAsync(data.AsMemory(0, data.Length), token).ConfigureAwait(false);
                await stream.FlushAsync(token).ConfigureAwait(false);
            }
        }

        private static void RestrictPermissions(string path)
        {
            if (!OperatingSystem.IsWindows())
                File.SetUnixFileMode(path, UnixFileMode.UserRead | UnixFileMode.UserWrite);
        }
    }
}
