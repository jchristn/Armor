namespace Armor.Core.Exceptions
{
    using System;

    /// <summary>
    /// Thrown when a backup cannot start because the same policy is already backing up — its cross-process
    /// run lock is held, most often by a scheduled run in the background agent. It is distinct from a
    /// failure: the existing run is proceeding normally, so a caller should say "already in progress" rather
    /// than report an error.
    /// </summary>
    public class PolicyAlreadyRunningException : ArmorException
    {
        /// <summary>
        /// Initializes a new instance of the <see cref="PolicyAlreadyRunningException"/> class.
        /// </summary>
        public PolicyAlreadyRunningException()
        {
        }

        /// <summary>
        /// Initializes a new instance of the <see cref="PolicyAlreadyRunningException"/> class with a message.
        /// </summary>
        /// <param name="message">The error message.</param>
        public PolicyAlreadyRunningException(string message)
            : base(message)
        {
        }

        /// <summary>
        /// Initializes a new instance of the <see cref="PolicyAlreadyRunningException"/> class with a message
        /// and an inner exception.
        /// </summary>
        /// <param name="message">The error message.</param>
        /// <param name="innerException">The exception that caused this exception.</param>
        public PolicyAlreadyRunningException(string message, Exception innerException)
            : base(message, innerException)
        {
        }
    }
}
