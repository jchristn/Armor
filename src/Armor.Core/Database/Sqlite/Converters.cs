namespace Armor.Core.Database.Sqlite
{
    using System;
    using System.Data;
    using System.Globalization;

    /// <summary>
    /// Reads strongly typed values out of a <see cref="DataRow"/> produced by the SQLite driver,
    /// treating <see cref="DBNull"/> as null and parsing timestamps as UTC. This type is stateless
    /// and thread-safe.
    /// </summary>
    public static class Converters
    {
        /// <summary>
        /// Read a required string column.
        /// </summary>
        /// <param name="row">The row.</param>
        /// <param name="column">The column name.</param>
        /// <returns>The string value, or empty string if the cell is null.</returns>
        /// <exception cref="ArgumentNullException">Thrown when <paramref name="row"/> is null.</exception>
        public static string GetString(DataRow row, string column)
        {
            if (row == null)
                throw new ArgumentNullException(nameof(row));
            object value = row[column];
            if (value == null || value == DBNull.Value)
                return String.Empty;
            return Convert.ToString(value, CultureInfo.InvariantCulture) ?? String.Empty;
        }

        /// <summary>
        /// Read a nullable string column.
        /// </summary>
        /// <param name="row">The row.</param>
        /// <param name="column">The column name.</param>
        /// <returns>The string value, or null if the cell is null.</returns>
        /// <exception cref="ArgumentNullException">Thrown when <paramref name="row"/> is null.</exception>
        public static string? GetStringOrNull(DataRow row, string column)
        {
            if (row == null)
                throw new ArgumentNullException(nameof(row));
            object value = row[column];
            if (value == null || value == DBNull.Value)
                return null;
            return Convert.ToString(value, CultureInfo.InvariantCulture);
        }

        /// <summary>
        /// Read a signed 64-bit integer column.
        /// </summary>
        /// <param name="row">The row.</param>
        /// <param name="column">The column name.</param>
        /// <returns>The integer value, or 0 if the cell is null.</returns>
        /// <exception cref="ArgumentNullException">Thrown when <paramref name="row"/> is null.</exception>
        public static long GetLong(DataRow row, string column)
        {
            if (row == null)
                throw new ArgumentNullException(nameof(row));
            object value = row[column];
            if (value == null || value == DBNull.Value)
                return 0;
            return Convert.ToInt64(value, CultureInfo.InvariantCulture);
        }

        /// <summary>
        /// Read a signed 32-bit integer column.
        /// </summary>
        /// <param name="row">The row.</param>
        /// <param name="column">The column name.</param>
        /// <returns>The integer value, or 0 if the cell is null.</returns>
        /// <exception cref="ArgumentNullException">Thrown when <paramref name="row"/> is null.</exception>
        public static int GetInt(DataRow row, string column)
        {
            return (int)GetLong(row, column);
        }

        /// <summary>
        /// Read a boolean column stored as an integer.
        /// </summary>
        /// <param name="row">The row.</param>
        /// <param name="column">The column name.</param>
        /// <returns>True if the stored integer is non-zero; otherwise false.</returns>
        /// <exception cref="ArgumentNullException">Thrown when <paramref name="row"/> is null.</exception>
        public static bool GetBool(DataRow row, string column)
        {
            return GetLong(row, column) != 0;
        }

        /// <summary>
        /// Read a required UTC timestamp column stored as ISO-8601 text.
        /// </summary>
        /// <param name="row">The row.</param>
        /// <param name="column">The column name.</param>
        /// <returns>The parsed UTC timestamp, or <see cref="DateTime.MinValue"/> (UTC) if the cell is null.</returns>
        /// <exception cref="ArgumentNullException">Thrown when <paramref name="row"/> is null.</exception>
        public static DateTime GetDateTime(DataRow row, string column)
        {
            string? text = GetStringOrNull(row, column);
            if (String.IsNullOrEmpty(text))
                return DateTime.SpecifyKind(DateTime.MinValue, DateTimeKind.Utc);
            return ParseUtc(text);
        }

        /// <summary>
        /// Read a nullable UTC timestamp column stored as ISO-8601 text.
        /// </summary>
        /// <param name="row">The row.</param>
        /// <param name="column">The column name.</param>
        /// <returns>The parsed UTC timestamp, or null if the cell is null.</returns>
        /// <exception cref="ArgumentNullException">Thrown when <paramref name="row"/> is null.</exception>
        public static DateTime? GetDateTimeOrNull(DataRow row, string column)
        {
            string? text = GetStringOrNull(row, column);
            if (String.IsNullOrEmpty(text))
                return null;
            return ParseUtc(text);
        }

        /// <summary>
        /// Read an enum column stored as its member name.
        /// </summary>
        /// <typeparam name="TEnum">The enum type.</typeparam>
        /// <param name="row">The row.</param>
        /// <param name="column">The column name.</param>
        /// <param name="fallback">Value returned when the cell is null or unparsable.</param>
        /// <returns>The parsed enum value, or <paramref name="fallback"/>.</returns>
        /// <exception cref="ArgumentNullException">Thrown when <paramref name="row"/> is null.</exception>
        public static TEnum GetEnum<TEnum>(DataRow row, string column, TEnum fallback) where TEnum : struct
        {
            string? text = GetStringOrNull(row, column);
            if (String.IsNullOrEmpty(text))
                return fallback;
            TEnum parsed;
            if (Enum.TryParse<TEnum>(text, false, out parsed))
                return parsed;
            return fallback;
        }

        private static DateTime ParseUtc(string text)
        {
            DateTime parsed = DateTime.Parse(text, CultureInfo.InvariantCulture, DateTimeStyles.AdjustToUniversal | DateTimeStyles.AssumeUniversal);
            return DateTime.SpecifyKind(parsed, DateTimeKind.Utc);
        }
    }
}
