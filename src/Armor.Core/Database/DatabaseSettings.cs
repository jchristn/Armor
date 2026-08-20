namespace Armor.Core.Database
{
    using System;

    /// <summary>
    /// Settings used to construct a database driver. For SQLite the only required value is the
    /// database file path.
    /// </summary>
    public class DatabaseSettings
    {
        private string _Filename = String.Empty;
        private int _BusyTimeoutMilliseconds = 5000;

        /// <summary>
        /// Database provider type. Default is <see cref="DatabaseTypeEnum.Sqlite"/>.
        /// </summary>
        public DatabaseTypeEnum Type { get; set; } = DatabaseTypeEnum.Sqlite;

        /// <summary>
        /// Path to the SQLite database file. Cannot be null or whitespace.
        /// </summary>
        /// <exception cref="ArgumentNullException">Thrown when set to null or whitespace.</exception>
        public string Filename
        {
            get
            {
                return _Filename;
            }
            set
            {
                if (String.IsNullOrWhiteSpace(value))
                    throw new ArgumentNullException(nameof(Filename));
                _Filename = value;
            }
        }

        /// <summary>
        /// SQLite busy timeout, in milliseconds, applied to the connection so concurrent access from
        /// a second process waits rather than failing immediately. Default is 5000. Clamped to the
        /// range 0 to 120000.
        /// </summary>
        public int BusyTimeoutMilliseconds
        {
            get
            {
                return _BusyTimeoutMilliseconds;
            }
            set
            {
                _BusyTimeoutMilliseconds = Math.Clamp(value, 0, 120000);
            }
        }

        /// <summary>
        /// Initializes a new instance of the <see cref="DatabaseSettings"/> class.
        /// </summary>
        public DatabaseSettings()
        {
        }

        /// <summary>
        /// Initializes a new instance of the <see cref="DatabaseSettings"/> class for SQLite.
        /// </summary>
        /// <param name="filename">Path to the SQLite database file. Cannot be null or whitespace.</param>
        /// <exception cref="ArgumentNullException">Thrown when <paramref name="filename"/> is null or whitespace.</exception>
        public DatabaseSettings(string filename)
        {
            Filename = filename;
        }
    }
}
