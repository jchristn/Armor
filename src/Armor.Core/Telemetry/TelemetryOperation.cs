namespace Armor.Core.Telemetry
{
    using System;
    using System.Diagnostics;
    using System.Diagnostics.Metrics;

    /// <summary>
    /// Times one discrete operation labeled by a single bounded dimension (a key operation, a recovery
    /// operation): it opens a span and, when disposed, records an outcome counter and a duration histogram,
    /// and counts failures in <see cref="TelemetryNames.Errors"/> under the given component. Call
    /// <see cref="Succeed"/> or <see cref="Fail"/> before disposing; an unsettled operation is recorded as a
    /// failure. Best-effort and never throws. Not thread-safe; use one instance per operation.
    /// </summary>
    internal sealed class TelemetryOperation : IDisposable
    {
        private readonly Counter<long> _Counter;
        private readonly Histogram<double> _Duration;
        private readonly string _LabelKey;
        private readonly string _LabelValue;
        private readonly string _Component;
        private readonly long _Start;
        private readonly Activity? _Activity;
        private string? _Outcome;
        private string? _ErrorType;
        private bool _Disposed;

        private TelemetryOperation(Counter<long> counter, Histogram<double> duration, string labelKey, string labelValue, string spanName, string component)
        {
            _Counter = counter;
            _Duration = duration;
            _LabelKey = labelKey;
            _LabelValue = labelValue;
            _Component = component;
            _Start = Stopwatch.GetTimestamp();
            _Activity = ArmorTelemetry.StartActivity(spanName);
            ArmorTelemetry.SetTag(_Activity, labelKey, labelValue);
        }

        internal Activity? Activity
        {
            get { return _Activity; }
        }

        internal static TelemetryOperation Start(Counter<long> counter, Histogram<double> duration, string labelKey, string labelValue, string spanName, string component)
        {
            return new TelemetryOperation(counter, duration, labelKey, labelValue, spanName, component);
        }

        internal void Succeed()
        {
            _Outcome = TelemetryNames.OutcomeSuccess;
            ArmorTelemetry.MarkSuccess(_Activity);
        }

        internal void Fail(Exception exception)
        {
            _Outcome = ArmorTelemetry.OutcomeOf(exception);
            if (_Outcome == TelemetryNames.OutcomeFailure)
            {
                _ErrorType = ArmorTelemetry.ErrorType(exception);
                ArmorTelemetry.RecordError(_Component, exception);
            }
            ArmorTelemetry.MarkException(_Activity, exception);
        }

        public void Dispose()
        {
            if (_Disposed)
                return;
            _Disposed = true;
            ArmorTelemetry.RecordOperation(_Counter, _Duration, _LabelKey, _LabelValue, _Outcome ?? TelemetryNames.OutcomeFailure, _ErrorType, Stopwatch.GetElapsedTime(_Start).TotalSeconds);
            ArmorTelemetry.Stop(_Activity);
        }
    }
}
