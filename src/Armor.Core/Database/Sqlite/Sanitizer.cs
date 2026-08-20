namespace Armor.Core.Database.Sqlite
{
    using System;
    using System.Globalization;
    using System.Text.RegularExpressions;

    /// <summary>
    /// Formats CLR values as SQLite literals for handwritten SQL. String values are escaped by
    /// doubling single quotes and stripping null characters; timestamps are written as round-trip
    /// UTC ISO-8601 text. Identifiers are validated against a strict character set. This type is
    /// stateless and thread-safe.
    /// </summary>
    public static class Sanitizer
    {
        private static readonly Regex _IdentifierPattern = new Regex("^[A-Za-z0-9_]+$", RegexOptions.Compiled);

        /// <summary>
        /// Format a nullable string as a quoted SQL literal, or <c>NULL</c> when null.
        /// </summary>
        /// <param name="value">The value, or null.</param>
        /// <returns>A quoted, escaped literal or <c>NULL</c>.</returns>
        public static string Quote(string? value)
        {
            if (value == null)
                return "NULL";
            return "'" + Escape(value) + "'";
        }

        /// <summary>
        /// Format a required string as a quoted SQL literal.
        /// </summary>
        /// <param name="value">The value. Cannot be null.</param>
        /// <returns>A quoted, escaped literal.</returns>
        /// <exception cref="ArgumentNullException">Thrown when <paramref name="value"/> is null.</exception>
        public static string Literal(string value)
        {
            if (value == null)
                throw new ArgumentNullException(nameof(value));
            return "'" + Escape(value) + "'";
        }

        /// <summary>
        /// Format a boolean as SQLite integer <c>0</c> or <c>1</c>.
        /// </summary>
        /// <param name="value">The value.</param>
        /// <returns><c>1</c> when true; otherwise <c>0</c>.</returns>
        public static string Bool(bool value)
        {
            return value ? "1" : "0";
        }

        /// <summary>
        /// Format a signed integer as a SQL literal.
        /// </summary>
        /// <param name="value">The value.</param>
        /// <returns>The integer as text.</returns>
        public static string Int(long value)
        {
            return value.ToString(CultureInfo.InvariantCulture);
        }

        /// <summary>
        /// Format a UTC timestamp as a quoted round-trip ISO-8601 literal.
        /// </summary>
        /// <param name="value">The timestamp. Interpreted as UTC.</param>
        /// <returns>A quoted ISO-8601 UTC literal.</returns>
        public static string Timestamp(DateTime value)
        {
            DateTime utc = value.Kind == DateTimeKind.Utc ? value : value.ToUniversalTime();
            return "'" + utc.ToString("yyyy-MM-ddTHH:mm:ss.fffffffZ", CultureInfo.InvariantCulture) + "'";
        }

        /// <summary>
        /// Format a nullable UTC timestamp as a quoted ISO-8601 literal, or <c>NULL</c> when null.
        /// </summary>
        /// <param name="value">The timestamp, or null.</param>
        /// <returns>A quoted ISO-8601 UTC literal or <c>NULL</c>.</returns>
        public static string TimestampNullable(DateTime? value)
        {
            if (value == null)
                return "NULL";
            return Timestamp(value.Value);
        }

        /// <summary>
        /// Validate that a string is a safe SQL identifier (letters, digits, and underscores only).
        /// </summary>
        /// <param name="identifier">The candidate identifier.</param>
        /// <returns>The identifier unchanged when valid.</returns>
        /// <exception cref="ArgumentNullException">Thrown when <paramref name="identifier"/> is null or whitespace.</exception>
        /// <exception cref="ArgumentException">Thrown when the identifier contains disallowed characters.</exception>
        public static string Identifier(string identifier)
        {
            if (String.IsNullOrWhiteSpace(identifier))
                throw new ArgumentNullException(nameof(identifier));
            if (!_IdentifierPattern.IsMatch(identifier))
                throw new ArgumentException("Invalid SQL identifier: '" + identifier + "'.", nameof(identifier));
            return identifier;
        }

        private static string Escape(string value)
        {
            return value.Replace("\0", String.Empty).Replace("'", "''");
        }
    }
}
