namespace Armor.Core.Configuration
{
    /// <summary>
    /// Logging configuration for Armor.
    /// </summary>
    public class LoggingSettings
    {
        /// <summary>
        /// Whether log messages are written to the console. Default is true.
        /// </summary>
        public bool ConsoleLogging { get; set; } = true;

        /// <summary>
        /// Whether log messages are written to files in the log directory. Default is true.
        /// </summary>
        public bool FileLogging { get; set; } = true;

        /// <summary>
        /// Initializes a new instance of the <see cref="LoggingSettings"/> class.
        /// </summary>
        public LoggingSettings()
        {
        }
    }
}
