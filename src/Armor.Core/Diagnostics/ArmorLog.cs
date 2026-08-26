namespace Armor.Core.Diagnostics
{
    using System;
    using System.Globalization;
    using System.IO;
    using System.Text;
    using System.Threading;
    using SyslogLogging;

    /// <summary>
    /// Central logging for Armor, backed by SyslogLogging's <see cref="LoggingModule"/>. Routine events go
    /// to a dated log file under the logs directory; unhandled failures are additionally written as
    /// standalone timestamped crash reports under the crash-logs directory. Console output is disabled for
    /// the terminal UI so log lines never corrupt the screen. Every member is safe to call before
    /// <see cref="Initialize"/> (it becomes a no-op) and from any thread; logging never throws.
    /// </summary>
    public static class ArmorLog
    {
        private static readonly object _Lock = new object();
        private static LoggingModule? _Log;
        private static string _CrashLogDirectory = String.Empty;

        /// <summary>
        /// Raised for every logged message (severity name, message) so a host — such as the TUI — can
        /// mirror log lines into its own on-screen log. Handlers run on the logging thread and must
        /// marshal to their UI thread themselves; a throwing handler is ignored.
        /// </summary>
        public static event Action<string, string>? MessageLogged;

        /// <summary>
        /// Initialize logging. Safe to call more than once; any prior logger is disposed first.
        /// </summary>
        /// <param name="logDirectory">Directory for the dated log file. Cannot be null or whitespace.</param>
        /// <param name="crashLogDirectory">Directory for crash reports. Cannot be null or whitespace.</param>
        /// <param name="applicationName">Name recorded on each log line. Cannot be null or whitespace.</param>
        /// <param name="enableConsole">Whether to also echo to the console. Pass false for the terminal UI.</param>
        /// <exception cref="ArgumentNullException">Thrown when a required argument is null or whitespace.</exception>
        public static void Initialize(string logDirectory, string crashLogDirectory, string applicationName, bool enableConsole)
        {
            if (String.IsNullOrWhiteSpace(logDirectory))
                throw new ArgumentNullException(nameof(logDirectory));
            if (String.IsNullOrWhiteSpace(crashLogDirectory))
                throw new ArgumentNullException(nameof(crashLogDirectory));
            if (String.IsNullOrWhiteSpace(applicationName))
                throw new ArgumentNullException(nameof(applicationName));

            lock (_Lock)
            {
                try
                {
                    _Log?.Dispose();
                }
                catch (Exception)
                {
                    // A failing dispose of the previous logger must not stop re-initialization.
                }

                Directory.CreateDirectory(logDirectory);
                Directory.CreateDirectory(crashLogDirectory);
                _CrashLogDirectory = crashLogDirectory;

                string filename = Path.Combine(logDirectory, "armor.log");
                LoggingModule module = new LoggingModule(filename, FileLoggingMode.FileWithDate, enableConsole);
                module.Settings.ApplicationName = applicationName;
                module.Settings.MinimumSeverity = Severity.Debug;
                module.Settings.EnableConsole = enableConsole;
                _Log = module;
            }
        }

        /// <summary>Log a debug-level message.</summary>
        /// <param name="message">The message.</param>
        public static void Debug(string message)
        {
            SafeLog(Severity.Debug, message);
        }

        /// <summary>Log an informational message.</summary>
        /// <param name="message">The message.</param>
        public static void Info(string message)
        {
            SafeLog(Severity.Info, message);
        }

        /// <summary>Log a warning.</summary>
        /// <param name="message">The message.</param>
        public static void Warn(string message)
        {
            SafeLog(Severity.Warn, message);
        }

        /// <summary>Log an error.</summary>
        /// <param name="message">The message.</param>
        public static void Error(string message)
        {
            SafeLog(Severity.Error, message);
        }

        /// <summary>Log an exception with its originating module and method.</summary>
        /// <param name="exception">The exception. Null is ignored.</param>
        /// <param name="module">The module or class where it occurred.</param>
        /// <param name="method">The method where it occurred.</param>
        public static void Exception(Exception exception, string module, string method)
        {
            if (exception == null)
                return;
            LoggingModule? log = _Log;
            if (log == null)
                return;
            try
            {
                log.Exception(exception, module ?? String.Empty, method ?? String.Empty);
            }
            catch (Exception)
            {
                // Logging must never throw into the caller.
            }
        }

        /// <summary>
        /// Write a standalone crash report for an unhandled failure and mirror it into the log.
        /// </summary>
        /// <param name="exception">The unhandled exception. Null is ignored.</param>
        /// <param name="context">A short description of what the app was doing.</param>
        /// <returns>The crash-report file path, or null when it could not be written.</returns>
        public static string? WriteCrash(Exception exception, string context)
        {
            if (exception == null)
                return null;

            string directory = _CrashLogDirectory;
            string? path = null;
            try
            {
                if (!String.IsNullOrWhiteSpace(directory))
                {
                    Directory.CreateDirectory(directory);
                    string stamp = DateTime.UtcNow.ToString("yyyyMMdd-HHmmss-fff", CultureInfo.InvariantCulture);
                    path = Path.Combine(directory, "crash-" + stamp + ".log");

                    StringBuilder report = new StringBuilder();
                    report.AppendLine("Armor crash report");
                    report.AppendLine("Time (UTC): " + DateTime.UtcNow.ToString("u", CultureInfo.InvariantCulture));
                    report.AppendLine("Context:    " + (context ?? String.Empty));
                    report.AppendLine("OS:         " + Environment.OSVersion);
                    report.AppendLine("Runtime:    " + Environment.Version);
                    report.AppendLine("64-bit:     " + Environment.Is64BitProcess);
                    report.AppendLine();
                    report.AppendLine(exception.ToString());

                    File.WriteAllText(path, report.ToString());
                }
            }
            catch (Exception)
            {
                path = null;
            }

            try
            {
                _Log?.Critical("Crash while " + (context ?? String.Empty) + (path != null ? " — report: " + path : String.Empty));
            }
            catch (Exception)
            {
                // Ignore secondary logging failures during crash handling.
            }

            Exception(exception, "Armor", context ?? "crash");
            Flush();
            return path;
        }

        /// <summary>Flush buffered log output to disk. Used before the process exits or on a crash.</summary>
        public static void Flush()
        {
            LoggingModule? log = _Log;
            if (log == null)
                return;
            try
            {
                log.FlushAsync(CancellationToken.None).GetAwaiter().GetResult();
            }
            catch (Exception)
            {
                // Best-effort flush.
            }
        }

        /// <summary>Dispose the logger, flushing any buffered output.</summary>
        public static void Dispose()
        {
            lock (_Lock)
            {
                try
                {
                    _Log?.Dispose();
                }
                catch (Exception)
                {
                    // Best-effort dispose.
                }
                _Log = null;
            }
        }

        private static void SafeLog(Severity severity, string message)
        {
            string text = message ?? String.Empty;
            LoggingModule? log = _Log;
            if (log != null)
            {
                try
                {
                    log.Log(severity, text);
                }
                catch (Exception)
                {
                    // Logging must never throw into the caller.
                }
            }

            Action<string, string>? sink = MessageLogged;
            if (sink != null)
            {
                try
                {
                    sink(severity.ToString(), text);
                }
                catch (Exception)
                {
                    // A misbehaving mirror handler must not affect logging.
                }
            }
        }
    }
}
