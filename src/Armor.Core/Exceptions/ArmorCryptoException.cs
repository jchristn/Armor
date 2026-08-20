namespace Armor.Core.Exceptions
{
    using System;

    /// <summary>
    /// Thrown when a cryptographic operation fails: authentication failure on decrypt, an unsupported
    /// frame version, a wrong passphrase or key file, or invalid key material. A failure here always
    /// aborts the operation rather than returning partial or unverified data.
    /// </summary>
    public class ArmorCryptoException : ArmorException
    {
        /// <summary>
        /// Initializes a new instance of the <see cref="ArmorCryptoException"/> class.
        /// </summary>
        public ArmorCryptoException()
        {
        }

        /// <summary>
        /// Initializes a new instance of the <see cref="ArmorCryptoException"/> class with a message.
        /// </summary>
        /// <param name="message">The error message.</param>
        public ArmorCryptoException(string message)
            : base(message)
        {
        }

        /// <summary>
        /// Initializes a new instance of the <see cref="ArmorCryptoException"/> class with a message
        /// and an inner exception.
        /// </summary>
        /// <param name="message">The error message.</param>
        /// <param name="innerException">The exception that caused this exception.</param>
        public ArmorCryptoException(string message, Exception innerException)
            : base(message, innerException)
        {
        }
    }
}
