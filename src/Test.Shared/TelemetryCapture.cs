namespace Test.Shared
{
    using System;
    using System.Collections.Concurrent;
    using System.Collections.Generic;
    using System.Diagnostics;
    using System.Diagnostics.Metrics;
    using System.Linq;
    using Armor.Core.Telemetry;

    /// <summary>
    /// In-memory telemetry listener for tests: subscribes to the Armor meter and activity source through
    /// the base class library (no exporter or SDK), records every measurement and every stopped span, and
    /// offers simple queries. Spans are scoped to the capture's own root trace (<see cref="Root"/>), so
    /// concurrent tests cannot pollute span assertions; metric assertions are "at least" checks because the
    /// meter is process-wide. Thread-safe. Dispose to unsubscribe.
    /// </summary>
    public sealed class TelemetryCapture : IDisposable
    {
        /// <summary>
        /// Name of the activity source the capture's root span is started on.
        /// </summary>
        public const string TestSourceName = "Armor.Tests";

        private static readonly ActivitySource _TestSource = new ActivitySource(TestSourceName);

        private readonly MeterListener _MeterListener;
        private readonly ActivityListener _ActivityListener;
        private readonly ConcurrentQueue<CapturedMeasurement> _Measurements = new ConcurrentQueue<CapturedMeasurement>();
        private readonly ConcurrentQueue<Activity> _Spans = new ConcurrentQueue<Activity>();
        private readonly Activity? _Root;

        /// <summary>
        /// The root span every Armor span started inside the capture's scope descends from.
        /// </summary>
        public Activity? Root
        {
            get { return _Root; }
        }

        /// <summary>
        /// Initializes a new instance of the <see cref="TelemetryCapture"/> class, starts listening, and starts
        /// the root span (which becomes <see cref="Activity.Current"/> for the caller's async flow).
        /// </summary>
        public TelemetryCapture()
        {
            _MeterListener = new MeterListener();
            _MeterListener.InstrumentPublished = (instrument, listener) =>
            {
                if (instrument.Meter.Name == TelemetryNames.MeterName)
                    listener.EnableMeasurementEvents(instrument);
            };
            _MeterListener.SetMeasurementEventCallback<long>((instrument, value, tags, state) => Add(instrument, value, tags));
            _MeterListener.SetMeasurementEventCallback<double>((instrument, value, tags, state) => Add(instrument, value, tags));
            _MeterListener.SetMeasurementEventCallback<int>((instrument, value, tags, state) => Add(instrument, value, tags));
            _MeterListener.Start();

            _ActivityListener = new ActivityListener();
            _ActivityListener.ShouldListenTo = source => source.Name == TelemetryNames.ActivitySourceName || source.Name == TestSourceName;
            _ActivityListener.Sample = (ref ActivityCreationOptions<ActivityContext> options) => ActivitySamplingResult.AllDataAndRecorded;
            _ActivityListener.ActivityStopped = activity => _Spans.Enqueue(activity);
            ActivitySource.AddActivityListener(_ActivityListener);

            _Root = _TestSource.StartActivity("test.root");
        }

        /// <summary>
        /// Collect every observable instrument (gauges) now.
        /// </summary>
        public void RecordObservables()
        {
            _MeterListener.RecordObservableInstruments();
        }

        /// <summary>
        /// All measurements recorded for an instrument, optionally filtered by tags that must all match.
        /// </summary>
        /// <param name="instrument">Instrument name.</param>
        /// <param name="tags">Alternating key, value pairs that must all be present.</param>
        /// <returns>The matching measurements.</returns>
        public List<CapturedMeasurement> Measurements(string instrument, params string[] tags)
        {
            return _Measurements.Where(m => m.Instrument == instrument && Matches(m.Tags, tags)).ToList();
        }

        /// <summary>
        /// The sum of matching measurements.
        /// </summary>
        /// <param name="instrument">Instrument name.</param>
        /// <param name="tags">Alternating key, value pairs that must all be present.</param>
        /// <returns>The sum.</returns>
        public double Sum(string instrument, params string[] tags)
        {
            return Measurements(instrument, tags).Sum(m => m.Value);
        }

        /// <summary>
        /// The number of matching measurements.
        /// </summary>
        /// <param name="instrument">Instrument name.</param>
        /// <param name="tags">Alternating key, value pairs that must all be present.</param>
        /// <returns>The count.</returns>
        public int Count(string instrument, params string[] tags)
        {
            return Measurements(instrument, tags).Count;
        }

        /// <summary>
        /// Stopped Armor spans in the capture's trace, optionally filtered by name.
        /// </summary>
        /// <param name="name">Span name, or null for all.</param>
        /// <returns>The matching spans.</returns>
        public List<Activity> Spans(string? name = null)
        {
            ActivityTraceId? trace = _Root?.TraceId;
            return _Spans.Where(a => a.Source.Name == TelemetryNames.ActivitySourceName
                && (trace == null || a.TraceId == trace.Value)
                && (name == null || a.OperationName == name)).ToList();
        }

        /// <summary>
        /// Stop the root span and unsubscribe.
        /// </summary>
        public void Dispose()
        {
            _Root?.Dispose();
            _ActivityListener.Dispose();
            _MeterListener.Dispose();
        }

        private void Add(Instrument instrument, double value, ReadOnlySpan<KeyValuePair<string, object?>> tags)
        {
            Dictionary<string, string> copy = new Dictionary<string, string>(StringComparer.Ordinal);
            foreach (KeyValuePair<string, object?> tag in tags)
                copy[tag.Key] = tag.Value?.ToString() ?? String.Empty;
            _Measurements.Enqueue(new CapturedMeasurement(instrument.Name, value, copy));
        }

        private static bool Matches(Dictionary<string, string> actual, string[] expected)
        {
            for (int i = 0; i + 1 < expected.Length; i += 2)
            {
                if (!actual.TryGetValue(expected[i], out string? value) || value != expected[i + 1])
                    return false;
            }
            return true;
        }
    }
}
