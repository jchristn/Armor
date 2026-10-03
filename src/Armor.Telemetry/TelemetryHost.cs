namespace Armor.Telemetry
{
    using System;
    using Armor.Core.Configuration;
    using Armor.Core.Diagnostics;
    using Armor.Core.Telemetry;
    using Microsoft.Extensions.Logging;
    using Radiant;

    /// <summary>
    /// The one telemetry host of an Armor process, started at the composition root of the agent and of the
    /// TUI. When <see cref="TelemetrySettings.Enabled"/> is true it starts a Radiant host subscribed to the
    /// <c>Armor</c> meter and activity source (plus .NET runtime and process metrics), exporting over OTLP,
    /// an optional in-process Prometheus endpoint, and optionally Loki, and it mirrors every
    /// <see cref="ArmorLog"/> line into the log pipeline so log records carry the active trace and span ids.
    /// When disabled, or when the exporter cannot start, the host is inert: the application runs exactly as
    /// before and nothing is exported. Dispose it on shutdown to flush pending telemetry. Thread-safe.
    /// </summary>
    public sealed class TelemetryHost : IDisposable
    {
        #region Public-Members

        /// <summary>
        /// Whether an exporter is running. False when telemetry is disabled or failed to start.
        /// </summary>
        public bool IsExporting
        {
            get { return _Host != null; }
        }

        /// <summary>
        /// The <c>service.name</c> this process reports, for example <c>armor-agent</c>. Null when inert.
        /// </summary>
        public string? ServiceName
        {
            get { return _ServiceName; }
        }

        /// <summary>
        /// The Prometheus scrape URL served by this process, or null when the endpoint is not enabled.
        /// </summary>
        public string? PrometheusUrl
        {
            get { return _PrometheusUrl; }
        }

        #endregion

        #region Private-Members

        private readonly object _Lock = new object();
        private RadiantHost? _Host;
        private ILogger? _Logger;
        private string? _ServiceName;
        private string? _PrometheusUrl;
        private bool _Disposed;

        #endregion

        #region Constructors-and-Factories

        private TelemetryHost()
        {
        }

        /// <summary>
        /// Start the telemetry host for this process. Never throws: a configuration or start-up failure is
        /// logged as a warning and an inert host is returned.
        /// </summary>
        /// <param name="settings">The telemetry settings. Null is treated as disabled.</param>
        /// <param name="role">The process role appended to the service name, for example "agent" or "tui". Cannot be null or whitespace.</param>
        /// <param name="prometheusPort">The Prometheus scrape port for this process (used only when the endpoint is enabled).</param>
        /// <returns>A started (or inert) host. Dispose it on shutdown.</returns>
        /// <exception cref="ArgumentNullException">Thrown when <paramref name="role"/> is null or whitespace.</exception>
        public static TelemetryHost Start(TelemetrySettings? settings, string role, int prometheusPort)
        {
            if (String.IsNullOrWhiteSpace(role))
                throw new ArgumentNullException(nameof(role));

            TelemetryHost host = new TelemetryHost();
            if (settings == null || !settings.Enabled)
                return host;

            try
            {
                RadiantSettings radiant = BuildSettings(settings, role, prometheusPort);
                host._Host = RadiantHost.Start(radiant);
                host._ServiceName = radiant.ServiceName;
                if (radiant.Prometheus.Enable)
                    host._PrometheusUrl = radiant.Prometheus.ToScrapeUrl();
                if (settings.LogsEnabled)
                {
                    host._Logger = host._Host.CreateLogger("Armor");
                    ArmorLog.MessageLogged += host.OnMessageLogged;
                }

                ArmorLog.Info("Telemetry export started as '" + radiant.ServiceName + "'"
                    + (radiant.Otlp.Enable ? ", OTLP " + radiant.Otlp.Protocol + " to " + radiant.Otlp.Endpoint : String.Empty)
                    + (host._PrometheusUrl != null ? ", Prometheus at " + host._PrometheusUrl : String.Empty)
                    + (radiant.Loki.Enable ? ", Loki at " + radiant.Loki.Endpoint : String.Empty)
                    + ".");
            }
            catch (Exception ex)
            {
                // Telemetry is best-effort: a bad endpoint or a port already in use must never stop Armor.
                ArmorLog.MessageLogged -= host.OnMessageLogged;
                try
                {
                    host._Host?.Dispose();
                }
                catch (Exception)
                {
                    // Best-effort cleanup of a half-started host.
                }
                host._Host = null;
                host._Logger = null;
                host._ServiceName = null;
                host._PrometheusUrl = null;
                ArmorLog.Warn("Telemetry export disabled: it could not start (" + ex.GetType().Name + ": " + ex.Message + ").");
            }
            return host;
        }

        #endregion

        #region Public-Methods

        /// <summary>
        /// Build the Radiant settings for a process from Armor's telemetry settings. Exposed so the mapping can
        /// be verified without starting an exporter.
        /// </summary>
        /// <param name="settings">The telemetry settings. Cannot be null.</param>
        /// <param name="role">The process role appended to the service name. Cannot be null or whitespace.</param>
        /// <param name="prometheusPort">The Prometheus scrape port for this process.</param>
        /// <returns>The Radiant settings.</returns>
        /// <exception cref="ArgumentNullException">Thrown when an argument is null or whitespace.</exception>
        public static RadiantSettings BuildSettings(TelemetrySettings settings, string role, int prometheusPort)
        {
            if (settings == null)
                throw new ArgumentNullException(nameof(settings));
            if (String.IsNullOrWhiteSpace(role))
                throw new ArgumentNullException(nameof(role));

            RadiantSettings radiant = new RadiantSettings(settings.ServiceName + "-" + role.Trim());
            radiant.Sources.AddMeter(TelemetryNames.MeterName);
            radiant.Sources.AddActivitySource(TelemetryNames.ActivitySourceName);

            radiant.Otlp.Enable = settings.OtlpEnabled;
            radiant.Otlp.Endpoint = settings.OtlpEndpoint;
            radiant.Otlp.Protocol = settings.OtlpProtocol == "httpprotobuf" ? OtlpProtocolEnum.HttpProtobuf : OtlpProtocolEnum.Grpc;

            radiant.Prometheus.Enable = settings.PrometheusEnabled;
            radiant.Prometheus.Hostname = settings.PrometheusHostname;
            radiant.Prometheus.Port = prometheusPort;

            radiant.Loki.Enable = settings.LogsEnabled && settings.LokiEnabled;
            radiant.Loki.Endpoint = settings.LokiEndpoint;

            radiant.Logs.Enable = settings.LogsEnabled;
            radiant.Traces.SamplingRatio = settings.TraceSamplingRatio;
            radiant.Metrics.ExportIntervalMs = settings.MetricsExportIntervalMs;
            radiant.Metrics.IncludeRuntime = true;
            radiant.Metrics.IncludeProcess = true;
            radiant.DiagnosticCallback = message => ArmorLog.Debug("Telemetry: " + message);
            return radiant;
        }

        /// <summary>
        /// Flush pending telemetry and stop the exporter. Safe to call more than once.
        /// </summary>
        public void Dispose()
        {
            RadiantHost? host;
            lock (_Lock)
            {
                if (_Disposed)
                    return;
                _Disposed = true;
                host = _Host;
                _Host = null;
                _Logger = null;
            }

            ArmorLog.MessageLogged -= OnMessageLogged;
            if (host == null)
                return;
            try
            {
                host.Dispose();
            }
            catch (Exception)
            {
                // Shutdown must never fail because an exporter could not flush.
            }
        }

        #endregion

        #region Private-Methods

        private void OnMessageLogged(string severity, string message)
        {
            ILogger? logger = _Logger;
            if (logger == null)
                return;
            try
            {
                // The raw-state overload keeps the message verbatim: Armor's lines are not message templates,
                // and a brace in a file path must not be parsed as a placeholder.
                logger.Log(MapLevel(severity), default(EventId), message, null, (state, error) => state);
            }
            catch (Exception)
            {
                // Mirroring a log line is best-effort.
            }
        }

        private static LogLevel MapLevel(string severity)
        {
            switch (severity)
            {
                case "Debug":
                    return LogLevel.Debug;
                case "Info":
                    return LogLevel.Information;
                case "Warn":
                    return LogLevel.Warning;
                case "Error":
                    return LogLevel.Error;
                case "Alert":
                case "Critical":
                case "Emergency":
                    return LogLevel.Critical;
                default:
                    return LogLevel.Information;
            }
        }

        #endregion
    }
}
