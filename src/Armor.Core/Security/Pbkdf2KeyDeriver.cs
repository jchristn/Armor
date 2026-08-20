namespace Armor.Core.Security
{
    using System;
    using System.Security.Cryptography;
    using System.Text;

    /// <summary>
    /// Derives a 256-bit key-encryption key from a passphrase using PBKDF2-HMAC-SHA256. The salt and
    /// iteration count are stored alongside the wrapped data key so the same key can be re-derived on
    /// any machine. This type is stateless and thread-safe.
    /// </summary>
    public static class Pbkdf2KeyDeriver
    {
        private const int KeyLengthBytes = 32;
        private const int DefaultSaltLengthBytes = 16;

        /// <summary>
        /// Derive a 32-byte key from a passphrase, salt, and iteration count.
        /// </summary>
        /// <param name="passphrase">The passphrase. Cannot be null or empty.</param>
        /// <param name="salt">The salt. Cannot be null or empty.</param>
        /// <param name="iterations">Iteration count. Must be positive.</param>
        /// <returns>The derived 32-byte key.</returns>
        /// <exception cref="ArgumentNullException">Thrown when <paramref name="passphrase"/> is null or empty, or <paramref name="salt"/> is null.</exception>
        /// <exception cref="ArgumentOutOfRangeException">Thrown when <paramref name="iterations"/> is not positive or <paramref name="salt"/> is empty.</exception>
        public static byte[] DeriveKey(string passphrase, byte[] salt, int iterations)
        {
            if (String.IsNullOrEmpty(passphrase))
                throw new ArgumentNullException(nameof(passphrase));
            if (salt == null)
                throw new ArgumentNullException(nameof(salt));
            if (salt.Length == 0)
                throw new ArgumentOutOfRangeException(nameof(salt), "Salt cannot be empty.");
            if (iterations <= 0)
                throw new ArgumentOutOfRangeException(nameof(iterations), "Iterations must be positive.");

            byte[] passwordBytes = Encoding.UTF8.GetBytes(passphrase);
            try
            {
                return Rfc2898DeriveBytes.Pbkdf2(passwordBytes, salt, iterations, HashAlgorithmName.SHA256, KeyLengthBytes);
            }
            finally
            {
                CryptographicOperations.ZeroMemory(passwordBytes);
            }
        }

        /// <summary>
        /// Generate a cryptographically random salt.
        /// </summary>
        /// <param name="lengthBytes">Salt length in bytes. Default is 16. Values below 8 are raised to 8.</param>
        /// <returns>A random salt.</returns>
        public static byte[] GenerateSalt(int lengthBytes = DefaultSaltLengthBytes)
        {
            int length = lengthBytes < 8 ? 8 : lengthBytes;
            return RandomNumberGenerator.GetBytes(length);
        }
    }
}
