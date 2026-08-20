namespace Armor.Core.Exceptions
{
    using System;

    /// <summary>
    /// Thrown when a storage-target operation fails: an unreachable target, a missing object, a
    /// connection-validation failure, or an unsupported target type.
    /// </summary>
    public class ArmorStorageException : ArmorException
    {
        /// <summary>
        /// Initializes a new instance of the <see cref="ArmorStorageException"/> class.
        /// </summary>
        public ArmorStorageException()
        {
        }

        /// <summary>
        /// Initializes a new instance of the <see cref="ArmorStorageException"/> class with a message.
        /// </summary>
        /// <param name="message">The error message.</param>
        public ArmorStorageException(string message)
            : base(message)
        {
        }

        /// <summary>
        /// Initializes a new instance of the <see cref="ArmorStorageException"/> class with a message
        /// and an inner exception.
        /// </summary>
        /// <param name="message">The error message.</param>
        /// <param name="innerException">The exception that caused this exception.</param>
        public ArmorStorageException(string message, Exception innerException)
            : base(message, innerException)
        {
        }
    }
}
