namespace Armor.Core.Exceptions
{
    using System;

    /// <summary>
    /// Thrown when a scheduled backup makes no progress for longer than the stall timeout — for example a
    /// network target that stopped answering, or a source read the operating system will not return from.
    /// The run is canceled and, if it does not stop promptly, abandoned, so the scheduler can keep running
    /// every other schedule.
    /// </summary>
    public class BackupStalledException : ArmorException
    {
        /// <summary>
        /// Initializes a new instance of the <see cref="BackupStalledException"/> class.
        /// </summary>
        public BackupStalledException()
        {
        }

        /// <summary>
        /// Initializes a new instance of the <see cref="BackupStalledException"/> class with a message.
        /// </summary>
        /// <param name="message">The error message.</param>
        public BackupStalledException(string message)
            : base(message)
        {
        }

        /// <summary>
        /// Initializes a new instance of the <see cref="BackupStalledException"/> class with a message
        /// and an inner exception.
        /// </summary>
        /// <param name="message">The error message.</param>
        /// <param name="innerException">The exception that caused this exception.</param>
        public BackupStalledException(string message, Exception innerException)
            : base(message, innerException)
        {
        }
    }
}
