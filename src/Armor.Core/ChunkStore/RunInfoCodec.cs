namespace Armor.Core.ChunkStore
{
    using System;
    using System.Text;
    using Armor.Core.Exceptions;
    using Armor.Core.Models;
    using Armor.Core.Security;
    using Armor.Core.Serialization;

    /// <summary>
    /// Encodes and decodes the per-run info sidecar for storage: the summary is serialized to JSON and
    /// encrypted with AES-256-GCM using the job identifier as associated data, wrapped in a one-byte
    /// frame-version header. Unlike the manifest it is not compressed — it is small by design. This type
    /// is stateless and thread-safe.
    /// </summary>
    public static class RunInfoCodec
    {
        private const byte FrameVersion = 1;
        private const int HeaderLengthBytes = 1;
        private const string AssociatedDataPrefix = "armor-info:";

        /// <summary>
        /// Encode a run-info summary into its stored representation.
        /// </summary>
        /// <param name="info">The summary. Cannot be null.</param>
        /// <param name="dataKey">The 32-byte repository data key. Cannot be null.</param>
        /// <returns>The stored info bytes.</returns>
        /// <exception cref="ArgumentNullException">Thrown when an argument is null.</exception>
        public static byte[] Encode(BackupRunInfo info, byte[] dataKey)
        {
            if (info == null)
                throw new ArgumentNullException(nameof(info));
            if (dataKey == null)
                throw new ArgumentNullException(nameof(dataKey));

            byte[] plaintext = Encoding.UTF8.GetBytes(ArmorJson.Serialize(info));
            byte[] associatedData = Encoding.UTF8.GetBytes(AssociatedDataPrefix + info.JobId);
            byte[] encrypted = AesGcmCipher.Encrypt(dataKey, plaintext, associatedData);

            byte[] stored = new byte[HeaderLengthBytes + encrypted.Length];
            stored[0] = FrameVersion;
            Buffer.BlockCopy(encrypted, 0, stored, HeaderLengthBytes, encrypted.Length);
            return stored;
        }

        /// <summary>
        /// Decode a stored run-info summary.
        /// </summary>
        /// <param name="stored">The stored info bytes. Cannot be null.</param>
        /// <param name="dataKey">The 32-byte repository data key. Cannot be null.</param>
        /// <param name="jobId">The job identifier used as associated data when encoding. Cannot be null or whitespace.</param>
        /// <returns>The decoded summary.</returns>
        /// <exception cref="ArgumentNullException">Thrown when an argument is null.</exception>
        /// <exception cref="ArmorCryptoException">Thrown when the sidecar is malformed, fails authentication, or cannot be deserialized.</exception>
        public static BackupRunInfo Decode(byte[] stored, byte[] dataKey, string jobId)
        {
            if (stored == null)
                throw new ArgumentNullException(nameof(stored));
            if (dataKey == null)
                throw new ArgumentNullException(nameof(dataKey));
            if (String.IsNullOrWhiteSpace(jobId))
                throw new ArgumentNullException(nameof(jobId));
            if (stored.Length < HeaderLengthBytes)
                throw new ArmorCryptoException("Stored run info is too short to be valid.");
            if (stored[0] != FrameVersion)
                throw new ArmorCryptoException("Unsupported stored run-info frame version: " + stored[0] + ".");

            byte[] encrypted = new byte[stored.Length - HeaderLengthBytes];
            Buffer.BlockCopy(stored, HeaderLengthBytes, encrypted, 0, encrypted.Length);

            byte[] associatedData = Encoding.UTF8.GetBytes(AssociatedDataPrefix + jobId);
            byte[] plaintext = AesGcmCipher.Decrypt(dataKey, encrypted, associatedData);

            BackupRunInfo? info;
            try
            {
                info = ArmorJson.Deserialize<BackupRunInfo>(Encoding.UTF8.GetString(plaintext));
            }
            catch (System.Text.Json.JsonException ex)
            {
                throw new ArmorCryptoException("Decoded run-info JSON is malformed.", ex);
            }

            if (info == null)
                throw new ArmorCryptoException("Decoded run info deserialized to null.");
            return info;
        }
    }
}
