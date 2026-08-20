namespace Armor.Core.ChunkStore
{
    using System;
    using System.Security.Cryptography;

    /// <summary>
    /// Computes SHA-256 content hashes. Chunk identity, and therefore deduplication, is defined by the
    /// lowercase hexadecimal SHA-256 of a chunk's plaintext. This type is stateless and thread-safe.
    /// </summary>
    public static class Hasher
    {
        /// <summary>
        /// Compute the lowercase hexadecimal SHA-256 hash of a byte array.
        /// </summary>
        /// <param name="data">The data to hash. Cannot be null.</param>
        /// <returns>The 64-character lowercase hexadecimal hash.</returns>
        /// <exception cref="ArgumentNullException">Thrown when <paramref name="data"/> is null.</exception>
        public static string Sha256Hex(byte[] data)
        {
            if (data == null)
                throw new ArgumentNullException(nameof(data));

            byte[] hash = SHA256.HashData(data);
            return Convert.ToHexString(hash).ToLowerInvariant();
        }

        /// <summary>
        /// Compute the lowercase hexadecimal SHA-256 hash of a string, using its UTF-8 encoding.
        /// </summary>
        /// <param name="text">The text to hash. Cannot be null.</param>
        /// <returns>The 64-character lowercase hexadecimal hash.</returns>
        /// <exception cref="ArgumentNullException">Thrown when <paramref name="text"/> is null.</exception>
        public static string Sha256HexOfText(string text)
        {
            if (text == null)
                throw new ArgumentNullException(nameof(text));

            return Sha256Hex(System.Text.Encoding.UTF8.GetBytes(text));
        }
    }
}
