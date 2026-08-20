namespace Armor.Core.ChunkStore
{
    using System;
    using System.Text;
    using Armor.Core.Enums;
    using Armor.Core.Exceptions;
    using Armor.Core.Models;
    using Armor.Core.Security;
    using Armor.Core.Serialization;

    /// <summary>
    /// Encodes and decodes manifests for storage: the manifest is serialized to JSON, compressed, and
    /// encrypted with AES-256-GCM using the job identifier as associated data, wrapped in a two-byte
    /// <c>[frameVersion][codec]</c> header. This type is stateless and thread-safe.
    /// </summary>
    public static class ManifestCodec
    {
        private const byte FrameVersion = 1;
        private const int HeaderLengthBytes = 2;
        private const string AssociatedDataPrefix = "armor-manifest:";

        /// <summary>
        /// Encode a manifest into its stored representation.
        /// </summary>
        /// <param name="manifest">The manifest. Cannot be null.</param>
        /// <param name="dataKey">The 32-byte repository data key. Cannot be null.</param>
        /// <returns>The stored manifest bytes.</returns>
        /// <exception cref="ArgumentNullException">Thrown when an argument is null.</exception>
        public static byte[] Encode(Manifest manifest, byte[] dataKey)
        {
            if (manifest == null)
                throw new ArgumentNullException(nameof(manifest));
            if (dataKey == null)
                throw new ArgumentNullException(nameof(dataKey));

            byte[] plaintext = Encoding.UTF8.GetBytes(ArmorJson.Serialize(manifest));
            CompressedBlock compressed = Compressor.Compress(plaintext);
            byte[] associatedData = Encoding.UTF8.GetBytes(AssociatedDataPrefix + manifest.JobId);
            byte[] encrypted = AesGcmCipher.Encrypt(dataKey, compressed.Data, associatedData);

            byte[] stored = new byte[HeaderLengthBytes + encrypted.Length];
            stored[0] = FrameVersion;
            stored[1] = (byte)compressed.Codec;
            Buffer.BlockCopy(encrypted, 0, stored, HeaderLengthBytes, encrypted.Length);
            return stored;
        }

        /// <summary>
        /// Decode a stored manifest back into a <see cref="Manifest"/>.
        /// </summary>
        /// <param name="stored">The stored manifest bytes. Cannot be null.</param>
        /// <param name="dataKey">The 32-byte repository data key. Cannot be null.</param>
        /// <param name="jobId">The job identifier used as associated data when encoding. Cannot be null or whitespace.</param>
        /// <returns>The decoded manifest.</returns>
        /// <exception cref="ArgumentNullException">Thrown when an argument is null.</exception>
        /// <exception cref="ArmorCryptoException">Thrown when the manifest is malformed, fails authentication, or cannot be deserialized.</exception>
        public static Manifest Decode(byte[] stored, byte[] dataKey, string jobId)
        {
            if (stored == null)
                throw new ArgumentNullException(nameof(stored));
            if (dataKey == null)
                throw new ArgumentNullException(nameof(dataKey));
            if (String.IsNullOrWhiteSpace(jobId))
                throw new ArgumentNullException(nameof(jobId));
            if (stored.Length < HeaderLengthBytes)
                throw new ArmorCryptoException("Stored manifest is too short to be valid.");
            if (stored[0] != FrameVersion)
                throw new ArmorCryptoException("Unsupported stored manifest frame version: " + stored[0] + ".");

            CompressionCodecEnum codec = (CompressionCodecEnum)stored[1];
            byte[] encrypted = new byte[stored.Length - HeaderLengthBytes];
            Buffer.BlockCopy(stored, HeaderLengthBytes, encrypted, 0, encrypted.Length);

            byte[] associatedData = Encoding.UTF8.GetBytes(AssociatedDataPrefix + jobId);
            byte[] compressed = AesGcmCipher.Decrypt(dataKey, encrypted, associatedData);
            byte[] plaintext = Compressor.Decompress(compressed, codec);

            Manifest? manifest;
            try
            {
                manifest = ArmorJson.Deserialize<Manifest>(Encoding.UTF8.GetString(plaintext));
            }
            catch (System.Text.Json.JsonException ex)
            {
                throw new ArmorCryptoException("Decoded manifest JSON is malformed.", ex);
            }

            if (manifest == null)
                throw new ArmorCryptoException("Decoded manifest deserialized to null.");
            return manifest;
        }
    }
}
