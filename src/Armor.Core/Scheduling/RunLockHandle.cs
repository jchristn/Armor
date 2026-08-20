namespace Armor.Core.Scheduling
{
    using System;
    using System.IO;

    /// <summary>
    /// A held run lock. Disposing it releases the underlying exclusive file lock, allowing another
    /// process or thread to acquire the same policy's lock.
    /// </summary>
    public sealed class RunLockHandle : IDisposable
    {
        private FileStream? _Stream;

        /// <summary>
        /// Initializes a new instance of the <see cref="RunLockHandle"/> class.
        /// </summary>
        /// <param name="stream">The exclusively opened lock-file stream. Cannot be null.</param>
        /// <exception cref="ArgumentNullException">Thrown when <paramref name="stream"/> is null.</exception>
        public RunLockHandle(FileStream stream)
        {
            _Stream = stream ?? throw new ArgumentNullException(nameof(stream));
        }

        /// <summary>
        /// Release the lock.
        /// </summary>
        public void Dispose()
        {
            if (_Stream != null)
            {
                _Stream.Dispose();
                _Stream = null;
            }
        }
    }
}
