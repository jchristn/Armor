namespace Armor.Core.Scheduling
{
    using System;
    using System.IO;

    /// <summary>
    /// A cross-process single-instance guard for the Armor agent, backed by an exclusively opened lock
    /// file in the state directory — the same mechanism as the per-policy <see cref="RunLock"/>. The agent
    /// holds the lock for its lifetime so only one agent runs at a time; the TUI probes it to decide
    /// whether it needs to start an agent for scheduled backups. This type is stateless and thread-safe.
    /// </summary>
    public static class AgentInstanceLock
    {
        /// <summary>
        /// Name of the agent single-instance lock file within the state directory.
        /// </summary>
        public const string LockFileName = "agent.lock";

        /// <summary>
        /// Attempt to acquire the agent single-instance lock without blocking. The agent calls this at
        /// startup and holds the returned handle for its whole lifetime; a second agent gets null and exits.
        /// </summary>
        /// <param name="stateDirectory">Directory where the lock file lives. Cannot be null or whitespace.</param>
        /// <returns>A handle to release on shutdown, or null if another agent already holds the lock.</returns>
        /// <exception cref="ArgumentNullException">Thrown when <paramref name="stateDirectory"/> is null or whitespace.</exception>
        public static RunLockHandle? TryAcquire(string stateDirectory)
        {
            if (String.IsNullOrWhiteSpace(stateDirectory))
                throw new ArgumentNullException(nameof(stateDirectory));

            Directory.CreateDirectory(stateDirectory);
            string lockPath = Path.Combine(stateDirectory, LockFileName);

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

        /// <summary>
        /// Determine whether an agent is currently running, by probing the single-instance lock. The probe
        /// acquires and immediately releases the lock, so it never keeps the agent from starting.
        /// </summary>
        /// <param name="stateDirectory">Directory where the lock file lives. Cannot be null or whitespace.</param>
        /// <returns>True when an agent holds the lock; otherwise false.</returns>
        /// <exception cref="ArgumentNullException">Thrown when <paramref name="stateDirectory"/> is null or whitespace.</exception>
        public static bool IsRunning(string stateDirectory)
        {
            RunLockHandle? handle = TryAcquire(stateDirectory);
            if (handle == null)
                return true;
            handle.Dispose();
            return false;
        }
    }
}
