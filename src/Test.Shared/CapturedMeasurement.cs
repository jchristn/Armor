namespace Test.Shared
{
    using System.Collections.Generic;

    /// <summary>
    /// One measurement recorded by <see cref="TelemetryCapture"/>: the instrument name, the value, and its tags.
    /// </summary>
    public sealed class CapturedMeasurement
    {
        /// <summary>
        /// Instrument name.
        /// </summary>
        public string Instrument { get; }

        /// <summary>
        /// Measured value.
        /// </summary>
        public double Value { get; }

        /// <summary>
        /// Tags, keyed by tag name, with values rendered as strings.
        /// </summary>
        public Dictionary<string, string> Tags { get; }

        /// <summary>
        /// Initializes a new instance of the <see cref="CapturedMeasurement"/> class.
        /// </summary>
        /// <param name="instrument">Instrument name.</param>
        /// <param name="value">Measured value.</param>
        /// <param name="tags">Tags.</param>
        public CapturedMeasurement(string instrument, double value, Dictionary<string, string> tags)
        {
            Instrument = instrument;
            Value = value;
            Tags = tags;
        }
    }
}
