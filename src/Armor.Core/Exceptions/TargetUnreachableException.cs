namespace Armor.Core.Exceptions
{
    using System;

    /// <summary>
    /// Thrown when a storage target cannot be reached right now for a reason that is expected to clear on
    /// its own — most commonly a removable/USB disk that is not currently connected. It is distinct from a
    /// genuine backup failure: an interactive caller should tell the user to reconnect the drive, while the
    /// scheduler treats it as "not yet" and leaves the schedule due so the backup runs the moment the target
    /// is reachable again, rather than recording a failure on every tick.
    /// </summary>
    public class TargetUnreachableException : ArmorException
    {
        /// <summary>
        /// Initializes a new instance of the <see cref="TargetUnreachableException"/> class.
        /// </summary>
        public TargetUnreachableException()
        {
        }

        /// <summary>
        /// Initializes a new instance of the <see cref="TargetUnreachableException"/> class with a message.
        /// </summary>
        /// <param name="message">The error message.</param>
        public TargetUnreachableException(string message)
            : base(message)
        {
        }

        /// <summary>
        /// Initializes a new instance of the <see cref="TargetUnreachableException"/> class with a message
        /// and an inner exception.
        /// </summary>
        /// <param name="message">The error message.</param>
        /// <param name="innerException">The exception that caused this exception.</param>
        public TargetUnreachableException(string message, Exception innerException)
            : base(message, innerException)
        {
        }
    }
}
