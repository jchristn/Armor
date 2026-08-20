namespace Armor.Core.Security
{
    using System;
    using System.Security.Cryptography;
    using Armor.Core.Exceptions;

    /// <summary>
    /// Authenticated encryption with AES-256-GCM. Each call generates a fresh random 96-bit nonce and
    /// produces a self-describing frame: a one-byte version, the nonce, the 128-bit authentication
    /// tag, and the ciphertext. Associated data (for example, a chunk's content hash) is bound into
    /// the tag so a ciphertext cannot be moved to a different logical location without detection.
    /// Decryption throws <see cref="ArmorCryptoException"/> if authentication fails. This type is
    /// stateless and thread-safe.
    /// </summary>
    public static class AesGcmCipher
    {
        private const byte FrameVersion = 1;
        private const int KeyLengthBytes = 32;
        private const int NonceLengthBytes = 12;
        private const int TagLengthBytes = 16;
        private const int HeaderLengthBytes = 1 + NonceLengthBytes + TagLengthBytes;

        /// <summary>
        /// Encrypt plaintext under a 256-bit key, returning a versioned frame containing the nonce,
        /// tag, and ciphertext.
        /// </summary>
        /// <param name="key">32-byte key. Cannot be null and must be exactly 32 bytes.</param>
        /// <param name="plaintext">The data to encrypt. Cannot be null; may be empty.</param>
        /// <param name="associatedData">Optional associated data bound into the tag. May be null.</param>
        /// <returns>The encrypted frame.</returns>
        /// <exception cref="ArgumentNullException">Thrown when <paramref name="key"/> or <paramref name="plaintext"/> is null.</exception>
        /// <exception cref="ArgumentException">Thrown when <paramref name="key"/> is not 32 bytes.</exception>
        public static byte[] Encrypt(byte[] key, byte[] plaintext, byte[]? associatedData)
        {
            if (key == null)
                throw new ArgumentNullException(nameof(key));
            if (plaintext == null)
                throw new ArgumentNullException(nameof(plaintext));
            if (key.Length != KeyLengthBytes)
                throw new ArgumentException("Key must be exactly " + KeyLengthBytes + " bytes.", nameof(key));

            byte[] nonce = RandomNumberGenerator.GetBytes(NonceLengthBytes);
            byte[] ciphertext = new byte[plaintext.Length];
            byte[] tag = new byte[TagLengthBytes];

            using (AesGcm gcm = new AesGcm(key, TagLengthBytes))
            {
                gcm.Encrypt(nonce, plaintext, ciphertext, tag, associatedData);
            }

            byte[] frame = new byte[HeaderLengthBytes + ciphertext.Length];
            frame[0] = FrameVersion;
            Buffer.BlockCopy(nonce, 0, frame, 1, NonceLengthBytes);
            Buffer.BlockCopy(tag, 0, frame, 1 + NonceLengthBytes, TagLengthBytes);
            Buffer.BlockCopy(ciphertext, 0, frame, HeaderLengthBytes, ciphertext.Length);
            return frame;
        }

        /// <summary>
        /// Decrypt and authenticate a frame produced by <see cref="Encrypt"/>.
        /// </summary>
        /// <param name="key">32-byte key. Cannot be null and must be exactly 32 bytes.</param>
        /// <param name="frame">The encrypted frame. Cannot be null.</param>
        /// <param name="associatedData">The same associated data supplied at encryption time, or null.</param>
        /// <returns>The decrypted plaintext.</returns>
        /// <exception cref="ArgumentNullException">Thrown when <paramref name="key"/> or <paramref name="frame"/> is null.</exception>
        /// <exception cref="ArgumentException">Thrown when <paramref name="key"/> is not 32 bytes.</exception>
        /// <exception cref="ArmorCryptoException">Thrown when the frame is malformed, has an unsupported version, or fails authentication.</exception>
        public static byte[] Decrypt(byte[] key, byte[] frame, byte[]? associatedData)
        {
            if (key == null)
                throw new ArgumentNullException(nameof(key));
            if (frame == null)
                throw new ArgumentNullException(nameof(frame));
            if (key.Length != KeyLengthBytes)
                throw new ArgumentException("Key must be exactly " + KeyLengthBytes + " bytes.", nameof(key));
            if (frame.Length < HeaderLengthBytes)
                throw new ArmorCryptoException("Encrypted frame is too short to be valid (" + frame.Length + " bytes).");
            if (frame[0] != FrameVersion)
                throw new ArmorCryptoException("Unsupported encrypted frame version: " + frame[0] + ".");

            byte[] nonce = new byte[NonceLengthBytes];
            byte[] tag = new byte[TagLengthBytes];
            int cipherLength = frame.Length - HeaderLengthBytes;
            byte[] ciphertext = new byte[cipherLength];

            Buffer.BlockCopy(frame, 1, nonce, 0, NonceLengthBytes);
            Buffer.BlockCopy(frame, 1 + NonceLengthBytes, tag, 0, TagLengthBytes);
            Buffer.BlockCopy(frame, HeaderLengthBytes, ciphertext, 0, cipherLength);

            byte[] plaintext = new byte[cipherLength];

            try
            {
                using (AesGcm gcm = new AesGcm(key, TagLengthBytes))
                {
                    gcm.Decrypt(nonce, ciphertext, tag, plaintext, associatedData);
                }
            }
            catch (CryptographicException ex)
            {
                throw new ArmorCryptoException("Decryption failed authentication; the data, key, or associated data is wrong or the data was tampered with.", ex);
            }

            return plaintext;
        }
    }
}
