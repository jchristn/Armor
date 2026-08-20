namespace Armor.Core.Exceptions
{
    using System;

    /// <summary>
    /// Thrown when Armor configuration cannot be loaded, parsed, or validated.
    /// </summary>
    public class ArmorConfigurationException : ArmorException
    {
        /// <summary>
        /// Initializes a new instance of the <see cref="ArmorConfigurationException"/> class.
        /// </summary>
        public ArmorConfigurationException()
        {
        }

        /// <summary>
        /// Initializes a new instance of the <see cref="ArmorConfigurationException"/> class with a
        /// message.
        /// </summary>
        /// <param name="message">The error message.</param>
        public ArmorConfigurationException(string message)
            : base(message)
        {
        }

        /// <summary>
        /// Initializes a new instance of the <see cref="ArmorConfigurationException"/> class with a
        /// message and an inner exception.
        /// </summary>
        /// <param name="message">The error message.</param>
        /// <param name="innerException">The exception that caused this exception.</param>
        public ArmorConfigurationException(string message, Exception innerException)
            : base(message, innerException)
        {
        }
    }
}
