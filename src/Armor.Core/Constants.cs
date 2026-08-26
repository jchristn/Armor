namespace Armor.Core
{
    /// <summary>
    /// Central, immutable constants shared across the Armor engine. Identifier prefixes are
    /// defined here so that every entity type has a single, authoritative, stable prefix.
    /// </summary>
    public static class Constants
    {
        /// <summary>
        /// Application name.
        /// </summary>
        public const string ApplicationName = "Armor";

        /// <summary>
        /// Application tagline.
        /// </summary>
        public const string Tagline = "Data protection for the paranoid.";

        /// <summary>
        /// Name of the Armor configuration directory created beneath the user's home directory.
        /// </summary>
        public const string ConfigDirectoryName = ".armor";

        /// <summary>
        /// Default configuration file name stored within the configuration directory.
        /// </summary>
        public const string ConfigFileName = "armor.json";

        /// <summary>
        /// Default log subdirectory name stored within the configuration directory.
        /// </summary>
        public const string LogDirectoryName = "logs";

        /// <summary>
        /// Subdirectory name (within the configuration directory) where crash reports are written.
        /// </summary>
        public const string CrashLogsDirectoryName = "crash-logs";

        /// <summary>
        /// Default state subdirectory name stored within the configuration directory.
        /// </summary>
        public const string StateDirectoryName = "state";

        /// <summary>
        /// Default SQLite database file name stored within the configuration directory.
        /// </summary>
        public const string DefaultDatabaseFileName = "armor.db";

        /// <summary>
        /// Identifier prefix for backup policies.
        /// </summary>
        public const string PolicyIdPrefix = "pol_";

        /// <summary>
        /// Identifier prefix for schedules.
        /// </summary>
        public const string ScheduleIdPrefix = "sch_";

        /// <summary>
        /// Identifier prefix for storage targets.
        /// </summary>
        public const string StorageTargetIdPrefix = "tgt_";

        /// <summary>
        /// Identifier prefix for encryption keys.
        /// </summary>
        public const string EncryptionKeyIdPrefix = "key_";

        /// <summary>
        /// Identifier prefix for backup jobs (runs / points-in-time).
        /// </summary>
        public const string BackupJobIdPrefix = "job_";

        /// <summary>
        /// Identifier prefix for restore jobs.
        /// </summary>
        public const string RestoreJobIdPrefix = "rst_";

        /// <summary>
        /// Identifier prefix for chunk index entries.
        /// </summary>
        public const string ChunkIdPrefix = "chk_";
    }
}
