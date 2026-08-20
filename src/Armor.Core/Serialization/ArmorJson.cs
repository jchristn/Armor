namespace Armor.Core.Serialization
{
    using System.Text.Json;
    using System.Text.Json.Serialization;

    /// <summary>
    /// Central JSON serialization settings for Armor. Enums serialize as strings, output is indented
    /// for human-editable files, and property names are case-insensitive on read for resilience.
    /// This type is thread-safe: the options instance is immutable after construction.
    /// </summary>
    public static class ArmorJson
    {
        private static readonly JsonSerializerOptions _Options = BuildOptions();

        /// <summary>
        /// The shared serializer options used for all Armor JSON documents.
        /// </summary>
        public static JsonSerializerOptions Options
        {
            get { return _Options; }
        }

        /// <summary>
        /// Serialize a value to indented JSON using the shared options.
        /// </summary>
        /// <typeparam name="T">The value type.</typeparam>
        /// <param name="value">The value to serialize.</param>
        /// <returns>The JSON representation.</returns>
        public static string Serialize<T>(T value)
        {
            return JsonSerializer.Serialize(value, _Options);
        }

        /// <summary>
        /// Deserialize JSON into a value using the shared options.
        /// </summary>
        /// <typeparam name="T">The target type.</typeparam>
        /// <param name="json">The JSON text.</param>
        /// <returns>The deserialized value, or null if the JSON is a null literal.</returns>
        public static T? Deserialize<T>(string json)
        {
            return JsonSerializer.Deserialize<T>(json, _Options);
        }

        private static JsonSerializerOptions BuildOptions()
        {
            JsonSerializerOptions options = new JsonSerializerOptions
            {
                WriteIndented = true,
                PropertyNameCaseInsensitive = true,
                DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
            };
            options.Converters.Add(new JsonStringEnumConverter());
            return options;
        }
    }
}
