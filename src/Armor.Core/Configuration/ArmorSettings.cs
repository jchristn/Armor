namespace Armor.Core.Configuration
{
    using System;

    /// <summary>
    /// Root configuration for Armor, serialized to <c>armor.json</c>. Values load from the file, are
    /// overridden by <c>ARMOR_*</c> environment variables, and are validated and clamped before use.
    /// </summary>
    public class ArmorSettings
    {
        private int _EngineConcurrency = 4;
        private int _SchedulerTickSeconds = 30;

        /// <summary>
        /// UTC timestamp when the configuration was created. Default is the current UTC time.
        /// </summary>
        public DateTime CreatedUtc { get; set; } = DateTime.UtcNow;

        /// <summary>
        /// Absolute or root-relative path to the SQLite database file. When null, the default path
        /// beneath the configuration root is used.
        /// </summary>
        public string? DatabaseFilename { get; set; } = null;

        /// <summary>
        /// Logging configuration. Never null; assigning null replaces it with defaults.
        /// </summary>
        public LoggingSettings Logging
        {
            get
            {
                return _Logging;
            }
            set
            {
                _Logging = value ?? new LoggingSettings();
            }
        }

        private LoggingSettings _Logging = new LoggingSettings();

        /// <summary>
        /// Content-defined chunking parameters. Never null; assigning null replaces it with defaults.
        /// </summary>
        public ChunkingSettings Chunking
        {
            get
            {
                return _Chunking;
            }
            set
            {
                _Chunking = value ?? new ChunkingSettings();
            }
        }

        private ChunkingSettings _Chunking = new ChunkingSettings();

        /// <summary>
        /// Maximum number of files processed concurrently by the backup engine. Default is 4. Clamped
        /// to the range 1 to 64.
        /// </summary>
        public int EngineConcurrency
        {
            get
            {
                return _EngineConcurrency;
            }
            set
            {
                _EngineConcurrency = Math.Clamp(value, 1, 64);
            }
        }

        /// <summary>
        /// Interval, in seconds, at which the agent evaluates schedules. Default is 30. Clamped to the
        /// range 5 to 3600.
        /// </summary>
        public int SchedulerTickSeconds
        {
            get
            {
                return _SchedulerTickSeconds;
            }
            set
            {
                _SchedulerTickSeconds = Math.Clamp(value, 5, 3600);
            }
        }

        /// <summary>
        /// Initializes a new instance of the <see cref="ArmorSettings"/> class with defaults.
        /// </summary>
        public ArmorSettings()
        {
        }

        /// <summary>
        /// Validate cross-field invariants. Individual scalar values are already clamped by their
        /// setters; this checks compound rules such as chunk-size ordering.
        /// </summary>
        /// <exception cref="ArgumentException">Thrown when a compound invariant is violated.</exception>
        public void Validate()
        {
            Chunking.Validate();
        }
    }
}
