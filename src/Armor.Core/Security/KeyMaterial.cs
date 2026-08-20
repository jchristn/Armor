namespace Armor.Core.Security
{
    using System;
    using System.Security.Cryptography;

    /// <summary>
    /// Helpers for generating and deriving raw key material. A repository data key is a random 256-bit
    /// value; a key file is arbitrary bytes from which a 256-bit key-encryption key is derived by
    /// SHA-256, so any key-file length is accepted while the resulting key is always 32 bytes. This
    /// type is stateless and thread-safe.
    /// </summary>
    public static class KeyMaterial
    {
        private const int DataKeyLengthBytes = 32;

        /// <summary>
        /// Generate a random 256-bit repository data key.
        /// </summary>
        /// <returns>A new 32-byte data key.</returns>
        public static byte[] GenerateDataKey()
        {
            return RandomNumberGenerator.GetBytes(DataKeyLengthBytes);
        }

        /// <summary>
        /// Generate random bytes suitable for writing to a key file.
        /// </summary>
        /// <param name="lengthBytes">Length in bytes. Default is 32. Values below 32 are raised to 32.</param>
        /// <returns>Random key-file bytes.</returns>
        public static byte[] GenerateKeyFileBytes(int lengthBytes = DataKeyLengthBytes)
        {
            int length = lengthBytes < DataKeyLengthBytes ? DataKeyLengthBytes : lengthBytes;
            return RandomNumberGenerator.GetBytes(length);
        }

        /// <summary>
        /// Derive a 256-bit key-encryption key from arbitrary key-file bytes using SHA-256.
        /// </summary>
        /// <param name="keyFileBytes">The key-file contents. Cannot be null or empty.</param>
        /// <returns>A 32-byte key-encryption key.</returns>
        /// <exception cref="ArgumentNullException">Thrown when <paramref name="keyFileBytes"/> is null.</exception>
        /// <exception cref="ArgumentException">Thrown when <paramref name="keyFileBytes"/> is empty.</exception>
        public static byte[] DeriveKeyFromKeyFile(byte[] keyFileBytes)
        {
            if (keyFileBytes == null)
                throw new ArgumentNullException(nameof(keyFileBytes));
            if (keyFileBytes.Length == 0)
                throw new ArgumentException("Key file cannot be empty.", nameof(keyFileBytes));

            return SHA256.HashData(keyFileBytes);
        }
    }
}
