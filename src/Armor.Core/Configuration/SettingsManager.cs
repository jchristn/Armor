namespace Armor.Core.Configuration
{
    using System;
    using System.IO;
    using System.Threading;
    using System.Threading.Tasks;
    using Armor.Core.Exceptions;
    using Armor.Core.Serialization;

    /// <summary>
    /// Loads, validates, and persists <see cref="ArmorSettings"/>. On first run the configuration
    /// directories are created and a default configuration file is written. Environment variables
    /// prefixed with <c>ARMOR_</c> override file values. This type is stateless and thread-safe.
    /// </summary>
    public class SettingsManager
    {
        private readonly ArmorPaths _Paths;
        private readonly Func<string, string?> _EnvironmentReader;

        /// <summary>
        /// Path resolver this manager reads from and writes to.
        /// </summary>
        public ArmorPaths Paths
        {
            get { return _Paths; }
        }

        /// <summary>
        /// Initializes a new instance of the <see cref="SettingsManager"/> class.
        /// </summary>
        /// <param name="paths">Path resolver. When null, a default <see cref="ArmorPaths"/> is used.</param>
        /// <param name="environmentReader">
        /// Function used to read environment variables during override application. When null, the
        /// process environment (<see cref="Environment.GetEnvironmentVariable(string)"/>) is used.
        /// Injecting a reader lets tests exercise overrides without mutating process-global state.
        /// </param>
        public SettingsManager(ArmorPaths? paths = null, Func<string, string?>? environmentReader = null)
        {
            _Paths = paths ?? new ArmorPaths();
            _EnvironmentReader = environmentReader ?? Environment.GetEnvironmentVariable;
        }

        /// <summary>
        /// Load configuration, creating the directory structure and a default configuration file if
        /// none exists. Environment overrides are applied and the result is validated.
        /// </summary>
        /// <param name="token">Cancellation token.</param>
        /// <returns>The effective configuration.</returns>
        /// <exception cref="ArmorConfigurationException">Thrown when the file cannot be parsed or fails validation.</exception>
        /// <exception cref="OperationCanceledException">Thrown when cancellation is requested.</exception>
        public async Task<ArmorSettings> LoadAsync(CancellationToken token = default)
        {
            token.ThrowIfCancellationRequested();
            _Paths.EnsureDirectories();

            ArmorSettings settings;

            if (!File.Exists(_Paths.ConfigFilePath))
            {
                settings = new ArmorSettings();
                settings.DatabaseFilename = _Paths.DefaultDatabasePath;
                await SaveAsync(settings, token).ConfigureAwait(false);
            }
            else
            {
                string json;
                using (StreamReader reader = new StreamReader(_Paths.ConfigFilePath))
                {
                    json = await reader.ReadToEndAsync().ConfigureAwait(false);
                }

                settings = Parse(json);
            }

            ApplyEnvironmentOverrides(settings);
            ApplyDefaults(settings);

            try
            {
                settings.Validate();
            }
            catch (ArgumentException ex)
            {
                throw new ArmorConfigurationException("Configuration failed validation: " + ex.Message, ex);
            }

            return settings;
        }

        /// <summary>
        /// Persist configuration to the configuration file, creating directories if needed.
        /// </summary>
        /// <param name="settings">The configuration to persist.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>A task that completes when the file has been written.</returns>
        /// <exception cref="ArgumentNullException">Thrown when <paramref name="settings"/> is null.</exception>
        /// <exception cref="OperationCanceledException">Thrown when cancellation is requested.</exception>
        public async Task SaveAsync(ArmorSettings settings, CancellationToken token = default)
        {
            if (settings == null)
                throw new ArgumentNullException(nameof(settings));

            token.ThrowIfCancellationRequested();
            _Paths.EnsureDirectories();

            string json = ArmorJson.Serialize(settings);

            using (StreamWriter writer = new StreamWriter(_Paths.ConfigFilePath, false))
            {
                await writer.WriteAsync(json.AsMemory(), token).ConfigureAwait(false);
            }
        }

        /// <summary>
        /// Parse configuration JSON into a settings object without touching the filesystem or applying
        /// overrides. Useful for validation and testing.
        /// </summary>
        /// <param name="json">The configuration JSON.</param>
        /// <returns>The parsed settings.</returns>
        /// <exception cref="ArgumentNullException">Thrown when <paramref name="json"/> is null or whitespace.</exception>
        /// <exception cref="ArmorConfigurationException">Thrown when the JSON cannot be parsed.</exception>
        public ArmorSettings Parse(string json)
        {
            if (String.IsNullOrWhiteSpace(json))
                throw new ArgumentNullException(nameof(json));

            try
            {
                ArmorSettings? parsed = ArmorJson.Deserialize<ArmorSettings>(json);
                if (parsed == null)
                    throw new ArmorConfigurationException("Configuration JSON deserialized to null.");
                return parsed;
            }
            catch (System.Text.Json.JsonException ex)
            {
                throw new ArmorConfigurationException("Configuration JSON is malformed: " + ex.Message, ex);
            }
        }

        /// <summary>
        /// Apply <c>ARMOR_*</c> environment-variable overrides onto a settings instance in place.
        /// </summary>
        /// <param name="settings">The settings to mutate.</param>
        /// <exception cref="ArgumentNullException">Thrown when <paramref name="settings"/> is null.</exception>
        public void ApplyEnvironmentOverrides(ArmorSettings settings)
        {
            if (settings == null)
                throw new ArgumentNullException(nameof(settings));

            string? dbFilename = _EnvironmentReader("ARMOR_DB_FILENAME");
            if (!String.IsNullOrWhiteSpace(dbFilename))
                settings.DatabaseFilename = dbFilename;

            bool consoleLogging;
            if (TryGetBool("ARMOR_LOG_CONSOLE", out consoleLogging))
                settings.Logging.ConsoleLogging = consoleLogging;

            bool fileLogging;
            if (TryGetBool("ARMOR_LOG_FILE", out fileLogging))
                settings.Logging.FileLogging = fileLogging;

            int concurrency;
            if (TryGetInt("ARMOR_ENGINE_CONCURRENCY", out concurrency))
                settings.EngineConcurrency = concurrency;

            int tick;
            if (TryGetInt("ARMOR_SCHEDULER_TICK_SECONDS", out tick))
                settings.SchedulerTickSeconds = tick;

            int chunkMin;
            if (TryGetInt("ARMOR_CHUNK_MIN_BYTES", out chunkMin))
                settings.Chunking.MinSizeBytes = chunkMin;

            int chunkAvg;
            if (TryGetInt("ARMOR_CHUNK_AVG_BYTES", out chunkAvg))
                settings.Chunking.AvgSizeBytes = chunkAvg;

            int chunkMax;
            if (TryGetInt("ARMOR_CHUNK_MAX_BYTES", out chunkMax))
                settings.Chunking.MaxSizeBytes = chunkMax;
        }

        private void ApplyDefaults(ArmorSettings settings)
        {
            if (String.IsNullOrWhiteSpace(settings.DatabaseFilename))
                settings.DatabaseFilename = _Paths.DefaultDatabasePath;
        }

        private bool TryGetBool(string name, out bool value)
        {
            value = false;
            string? raw = _EnvironmentReader(name);
            if (String.IsNullOrWhiteSpace(raw))
                return false;
            return bool.TryParse(raw, out value);
        }

        private bool TryGetInt(string name, out int value)
        {
            value = 0;
            string? raw = _EnvironmentReader(name);
            if (String.IsNullOrWhiteSpace(raw))
                return false;
            return int.TryParse(raw, out value);
        }
    }
}
