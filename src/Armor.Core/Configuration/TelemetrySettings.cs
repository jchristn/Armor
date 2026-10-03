namespace Armor.Core.Configuration
{
    using System;

    /// <summary>
    /// Telemetry export settings, the <c>Telemetry</c> section of <c>armor.json</c>. Armor always emits
    /// metrics and spans through the base class library (the <c>Armor</c> meter and activity source), which
    /// costs next to nothing when nobody listens. These settings control whether the agent and the TUI
    /// host an exporter that ships that data (plus runtime metrics and logs) to an OpenTelemetry collector,
    /// a Prometheus scrape endpoint, or Loki. Export is off by default because Armor runs on end-user
    /// desktops where no collector is expected; every endpoint defaults to the loopback address
    /// <c>127.0.0.1</c>, never <c>localhost</c>.
    /// </summary>
    public class TelemetrySettings
    {
        #region Public-Members

        /// <summary>
        /// Whether the agent and the TUI start a telemetry exporter. Default is false. When false, nothing is
        /// exported, no port is opened, and no connection is attempted.
        /// </summary>
        public bool Enabled { get; set; } = false;

        /// <summary>
        /// Base service name reported as <c>service.name</c>. Each process appends its role, so the agent
        /// reports <c>armor-agent</c> and the TUI <c>armor-tui</c> by default. Never null or whitespace;
        /// assigning one restores the default "armor".
        /// </summary>
        public string ServiceName
        {
            get
            {
                return _ServiceName;
            }
            set
            {
                _ServiceName = String.IsNullOrWhiteSpace(value) ? "armor" : value.Trim();
            }
        }

        /// <summary>
        /// Whether metrics, traces, and logs are pushed over OTLP to <see cref="OtlpEndpoint"/>. Default is true
        /// (effective only when <see cref="Enabled"/> is true).
        /// </summary>
        public bool OtlpEnabled { get; set; } = true;

        /// <summary>
        /// OTLP endpoint, normally an OpenTelemetry Collector. Default is <c>http://127.0.0.1:4317</c> (gRPC).
        /// For OTLP over HTTP use port 4318 and set <see cref="OtlpProtocol"/> to "httpprotobuf". Never null or
        /// whitespace; assigning one restores the default.
        /// </summary>
        public string OtlpEndpoint
        {
            get
            {
                return _OtlpEndpoint;
            }
            set
            {
                _OtlpEndpoint = String.IsNullOrWhiteSpace(value) ? DefaultOtlpEndpoint : value.Trim();
            }
        }

        /// <summary>
        /// OTLP protocol: "grpc" (default) or "httpprotobuf". Any other value is treated as "grpc".
        /// </summary>
        public string OtlpProtocol
        {
            get
            {
                return _OtlpProtocol;
            }
            set
            {
                _OtlpProtocol = String.Equals(value?.Trim(), "httpprotobuf", StringComparison.OrdinalIgnoreCase) ? "httpprotobuf" : "grpc";
            }
        }

        /// <summary>
        /// Whether logs written through Armor's log are also exported (over OTLP, and to Loki when
        /// <see cref="LokiEnabled"/> is true), stamped with the active trace and span ids. Default is true.
        /// </summary>
        public bool LogsEnabled { get; set; } = true;

        /// <summary>
        /// Whether each process serves an in-process Prometheus scrape endpoint. Default is false (the
        /// collector route is preferred because both Armor processes run on the host, not in a container).
        /// </summary>
        public bool PrometheusEnabled { get; set; } = false;

        /// <summary>
        /// Interface the Prometheus endpoint binds to. Default is <c>127.0.0.1</c>; the endpoint is anonymous,
        /// so do not bind it to a public interface. Never null or whitespace; assigning one restores the default.
        /// </summary>
        public string PrometheusHostname
        {
            get
            {
                return _PrometheusHostname;
            }
            set
            {
                _PrometheusHostname = String.IsNullOrWhiteSpace(value) ? "127.0.0.1" : value.Trim();
            }
        }

        /// <summary>
        /// Prometheus scrape port for the agent. Default is 9464. Clamped to the range 1 to 65535.
        /// </summary>
        public int AgentPrometheusPort
        {
            get
            {
                return _AgentPrometheusPort;
            }
            set
            {
                _AgentPrometheusPort = Math.Clamp(value, 1, 65535);
            }
        }

        /// <summary>
        /// Prometheus scrape port for the TUI. Default is 9465 (distinct from the agent so both can run at
        /// once). Clamped to the range 1 to 65535.
        /// </summary>
        public int TuiPrometheusPort
        {
            get
            {
                return _TuiPrometheusPort;
            }
            set
            {
                _TuiPrometheusPort = Math.Clamp(value, 1, 65535);
            }
        }

        /// <summary>
        /// Whether logs are also pushed directly to Loki's OTLP endpoint (<see cref="LokiEndpoint"/>), bypassing
        /// the collector. Default is false; the bundled collector already forwards OTLP logs to Loki.
        /// </summary>
        public bool LokiEnabled { get; set; } = false;

        /// <summary>
        /// Loki OTLP base endpoint. Default is <c>http://127.0.0.1:3100/otlp</c>. Never null or whitespace;
        /// assigning one restores the default.
        /// </summary>
        public string LokiEndpoint
        {
            get
            {
                return _LokiEndpoint;
            }
            set
            {
                _LokiEndpoint = String.IsNullOrWhiteSpace(value) ? DefaultLokiEndpoint : value.Trim();
            }
        }

        /// <summary>
        /// Fraction of root traces sampled, parent-based. Default is 1.0 (every backup is traced). Clamped to
        /// the range 0 to 1.
        /// </summary>
        public double TraceSamplingRatio
        {
            get
            {
                return _TraceSamplingRatio;
            }
            set
            {
                _TraceSamplingRatio = Double.IsNaN(value) ? 1.0 : Math.Clamp(value, 0.0, 1.0);
            }
        }

        /// <summary>
        /// How often metrics are pushed over OTLP, in milliseconds. Default is 15000. Clamped to the range
        /// 1000 to 300000.
        /// </summary>
        public int MetricsExportIntervalMs
        {
            get
            {
                return _MetricsExportIntervalMs;
            }
            set
            {
                _MetricsExportIntervalMs = Math.Clamp(value, 1000, 300000);
            }
        }

        /// <summary>
        /// Whether every chunk-level storage call gets its own client span. Default is false: a large backup
        /// makes millions of chunk calls, so they are covered by metrics only. Enable briefly to diagnose
        /// individual chunk latencies. Applies even when <see cref="Enabled"/> is false (to any listener).
        /// </summary>
        public bool TraceChunkOperations { get; set; } = false;

        #endregion

        #region Private-Members

        private const string DefaultOtlpEndpoint = "http://127.0.0.1:4317";
        private const string DefaultLokiEndpoint = "http://127.0.0.1:3100/otlp";

        private string _ServiceName = "armor";
        private string _OtlpEndpoint = DefaultOtlpEndpoint;
        private string _OtlpProtocol = "grpc";
        private string _PrometheusHostname = "127.0.0.1";
        private int _AgentPrometheusPort = 9464;
        private int _TuiPrometheusPort = 9465;
        private string _LokiEndpoint = DefaultLokiEndpoint;
        private double _TraceSamplingRatio = 1.0;
        private int _MetricsExportIntervalMs = 15000;

        #endregion

        #region Constructors-and-Factories

        /// <summary>
        /// Initializes a new instance of the <see cref="TelemetrySettings"/> class with defaults.
        /// </summary>
        public TelemetrySettings()
        {
        }

        #endregion
    }
}
