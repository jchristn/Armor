namespace Armor.Core.Scheduling
{
    using System;
    using System.IO;
    using System.Text.RegularExpressions;

    /// <summary>
    /// A cross-process, per-policy run lock backed by an exclusively opened lock file in the state
    /// directory. A scheduled run and a manually triggered run of the same policy cannot both hold the
    /// lock, so a policy never backs up to the same repository twice at once. Locks held by other
    /// processes are honored because the operating system enforces the exclusive open. This type is
    /// thread-safe.
    /// </summary>
    public sealed class RunLock
    {
        private static readonly Regex _SafeName = new Regex("^[A-Za-z0-9_]+$", RegexOptions.Compiled);

        private readonly string _StateDirectory;

        /// <summary>
        /// Initializes a new instance of the <see cref="RunLock"/> class.
        /// </summary>
        /// <param name="stateDirectory">Directory where lock files are created. Cannot be null or whitespace.</param>
        /// <exception cref="ArgumentNullException">Thrown when <paramref name="stateDirectory"/> is null or whitespace.</exception>
        public RunLock(string stateDirectory)
        {
            if (String.IsNullOrWhiteSpace(stateDirectory))
                throw new ArgumentNullException(nameof(stateDirectory));
            _StateDirectory = stateDirectory;
        }

        /// <summary>
        /// Attempt to acquire the lock for a policy without blocking.
        /// </summary>
        /// <param name="policyId">Policy identifier (letters, digits, and underscores only). Cannot be null or whitespace.</param>
        /// <returns>A handle to release on disposal, or null if the lock is already held.</returns>
        /// <exception cref="ArgumentNullException">Thrown when <paramref name="policyId"/> is null or whitespace.</exception>
        /// <exception cref="ArgumentException">Thrown when <paramref name="policyId"/> contains unsafe characters.</exception>
        public RunLockHandle? TryAcquire(string policyId)
        {
            if (String.IsNullOrWhiteSpace(policyId))
                throw new ArgumentNullException(nameof(policyId));
            if (!_SafeName.IsMatch(policyId))
                throw new ArgumentException("Policy id contains characters that are not safe for a lock file name.", nameof(policyId));

            Directory.CreateDirectory(_StateDirectory);
            string lockPath = Path.Combine(_StateDirectory, policyId + ".lock");

            try
            {
                FileStream stream = new FileStream(lockPath, FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None);
                return new RunLockHandle(stream);
            }
            catch (IOException)
            {
                return null;
            }
            catch (UnauthorizedAccessException)
            {
                return null;
            }
        }
    }
}
