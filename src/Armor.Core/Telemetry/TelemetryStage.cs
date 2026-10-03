namespace Armor.Core.Telemetry
{
    using System;
    using System.Diagnostics;
    using System.Diagnostics.Metrics;

    /// <summary>
    /// Times one pipeline stage: it opens a <c>stage:&lt;name&gt;</c> span under the current activity and,
    /// when disposed, records the stage's duration histogram and execution counter with its outcome. Call
    /// <see cref="Succeed"/> or <see cref="Fail"/> before disposing; a stage disposed unsettled is recorded
    /// as a failure. Best-effort and never throws. Not thread-safe; use one instance per stage execution.
    /// </summary>
    internal sealed class TelemetryStage : IDisposable
    {
        private readonly Counter<long> _Counter;
        private readonly Histogram<double> _Duration;
        private readonly string _Stage;
        private readonly long _Start;
        private readonly Activity? _Activity;
        private string? _Outcome;
        private string? _ErrorType;
        private bool _Disposed;

        private TelemetryStage(Counter<long> counter, Histogram<double> duration, string stage)
        {
            _Counter = counter;
            _Duration = duration;
            _Stage = stage;
            _Start = Stopwatch.GetTimestamp();
            _Activity = ArmorTelemetry.StartActivity(TelemetryNames.SpanStagePrefix + stage);
            ArmorTelemetry.SetTag(_Activity, TelemetryNames.AttrStage, stage);
        }

        internal Activity? Activity
        {
            get { return _Activity; }
        }

        internal static TelemetryStage Start(Counter<long> counter, Histogram<double> duration, string stage)
        {
            return new TelemetryStage(counter, duration, stage);
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
                _ErrorType = ArmorTelemetry.ErrorType(exception);
            ArmorTelemetry.MarkException(_Activity, exception);
        }

        public void Dispose()
        {
            if (_Disposed)
                return;
            _Disposed = true;

            string outcome = _Outcome ?? TelemetryNames.OutcomeFailure;
            TagList tags = new TagList();
            tags.Add(TelemetryNames.AttrStage, _Stage);
            tags.Add(TelemetryNames.AttrOutcome, outcome);
            ArmorTelemetry.Record(_Duration, Stopwatch.GetElapsedTime(_Start).TotalSeconds, tags);
            if (_ErrorType != null)
                tags.Add(TelemetryNames.AttrErrorType, _ErrorType);
            ArmorTelemetry.Add(_Counter, 1, tags);
            ArmorTelemetry.Stop(_Activity);
        }
    }
}
