namespace Armor.Core.Exceptions
{
    using System;

    /// <summary>
    /// Base type for all Armor domain exceptions. Catch this to handle any error originating from the
    /// Armor engine.
    /// </summary>
    public class ArmorException : Exception
    {
        /// <summary>
        /// Initializes a new instance of the <see cref="ArmorException"/> class.
        /// </summary>
        public ArmorException()
        {
        }

        /// <summary>
        /// Initializes a new instance of the <see cref="ArmorException"/> class with a message.
        /// </summary>
        /// <param name="message">The error message.</param>
        public ArmorException(string message)
            : base(message)
        {
        }

        /// <summary>
        /// Initializes a new instance of the <see cref="ArmorException"/> class with a message and an
        /// inner exception.
        /// </summary>
        /// <param name="message">The error message.</param>
        /// <param name="innerException">The exception that caused this exception.</param>
        public ArmorException(string message, Exception innerException)
            : base(message, innerException)
        {
        }
    }
}
