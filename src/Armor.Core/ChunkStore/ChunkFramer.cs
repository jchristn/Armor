namespace Armor.Core.ChunkStore
{
    using System;
    using Armor.Core.Enums;
    using Armor.Core.Exceptions;
    using Armor.Core.Security;

    /// <summary>
    /// Frames and unframes stored chunks. A stored chunk is the chunk's plaintext, compressed with the
    /// best available codec, then encrypted with AES-256-GCM using the chunk's content hash as
    /// associated data, wrapped in a two-byte header of <c>[frameVersion][codec]</c>. Unframing
    /// decrypts, decompresses, and re-verifies the content hash, so a corrupt or substituted chunk is
    /// rejected rather than returned. This type is stateless and thread-safe.
    /// </summary>
    public static class ChunkFramer
    {
        private const byte FrameVersion = 1;
        private const int HeaderLengthBytes = 2;

        /// <summary>
        /// Frame a plaintext chunk into its stored representation.
        /// </summary>
        /// <param name="plaintext">The chunk's plaintext bytes. Cannot be null.</param>
        /// <param name="dataKey">The 32-byte repository data key. Cannot be null.</param>
        /// <param name="hashHex">The lowercase hexadecimal SHA-256 hash of <paramref name="plaintext"/>. Cannot be null or whitespace.</param>
        /// <returns>The stored chunk bytes.</returns>
        /// <exception cref="ArgumentNullException">Thrown when <paramref name="plaintext"/>, <paramref name="dataKey"/>, or <paramref name="hashHex"/> is null.</exception>
        public static byte[] Frame(byte[] plaintext, byte[] dataKey, string hashHex)
        {
            if (plaintext == null)
                throw new ArgumentNullException(nameof(plaintext));
            if (dataKey == null)
                throw new ArgumentNullException(nameof(dataKey));
            if (String.IsNullOrWhiteSpace(hashHex))
                throw new ArgumentNullException(nameof(hashHex));

            CompressedBlock compressed = Compressor.Compress(plaintext);
            byte[] associatedData = Convert.FromHexString(hashHex);
            byte[] encrypted = AesGcmCipher.Encrypt(dataKey, compressed.Data, associatedData);

            byte[] stored = new byte[HeaderLengthBytes + encrypted.Length];
            stored[0] = FrameVersion;
            stored[1] = (byte)compressed.Codec;
            Buffer.BlockCopy(encrypted, 0, stored, HeaderLengthBytes, encrypted.Length);
            return stored;
        }

        /// <summary>
        /// Unframe a stored chunk back to its plaintext, verifying its content hash.
        /// </summary>
        /// <param name="stored">The stored chunk bytes. Cannot be null.</param>
        /// <param name="dataKey">The 32-byte repository data key. Cannot be null.</param>
        /// <param name="expectedHashHex">The expected lowercase hexadecimal SHA-256 content hash. Cannot be null or whitespace.</param>
        /// <returns>The chunk's plaintext bytes.</returns>
        /// <exception cref="ArgumentNullException">Thrown when an argument is null.</exception>
        /// <exception cref="ArmorCryptoException">Thrown when the frame is malformed, fails authentication, cannot be decompressed, or the recomputed hash does not match.</exception>
        public static byte[] Unframe(byte[] stored, byte[] dataKey, string expectedHashHex)
        {
            if (stored == null)
                throw new ArgumentNullException(nameof(stored));
            if (dataKey == null)
                throw new ArgumentNullException(nameof(dataKey));
            if (String.IsNullOrWhiteSpace(expectedHashHex))
                throw new ArgumentNullException(nameof(expectedHashHex));
            if (stored.Length < HeaderLengthBytes)
                throw new ArmorCryptoException("Stored chunk is too short to be valid (" + stored.Length + " bytes).");
            if (stored[0] != FrameVersion)
                throw new ArmorCryptoException("Unsupported stored chunk frame version: " + stored[0] + ".");

            CompressionCodecEnum codec = (CompressionCodecEnum)stored[1];
            byte[] encrypted = new byte[stored.Length - HeaderLengthBytes];
            Buffer.BlockCopy(stored, HeaderLengthBytes, encrypted, 0, encrypted.Length);

            byte[] associatedData = Convert.FromHexString(expectedHashHex);
            byte[] compressed = AesGcmCipher.Decrypt(dataKey, encrypted, associatedData);
            byte[] plaintext = Compressor.Decompress(compressed, codec);

            string actualHash = Hasher.Sha256Hex(plaintext);
            if (!String.Equals(actualHash, expectedHashHex, StringComparison.OrdinalIgnoreCase))
                throw new ArmorCryptoException("Chunk content hash mismatch after decryption; expected " + expectedHashHex + " but computed " + actualHash + ".");

            return plaintext;
        }
    }
}
