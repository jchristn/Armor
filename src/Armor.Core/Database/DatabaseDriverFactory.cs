namespace Armor.Core.Database
{
    using System;
    using System.Threading;
    using System.Threading.Tasks;
    using Armor.Core.Database.Sqlite;

    /// <summary>
    /// Composition root for the data layer. Creates the appropriate <see cref="DatabaseDriverBase"/>
    /// for a <see cref="DatabaseSettings"/> instance.
    /// </summary>
    public static class DatabaseDriverFactory
    {
        /// <summary>
        /// Create a driver for the given settings without initializing it.
        /// </summary>
        /// <param name="settings">Database settings.</param>
        /// <returns>An uninitialized driver.</returns>
        /// <exception cref="ArgumentNullException">Thrown when <paramref name="settings"/> is null.</exception>
        /// <exception cref="ArgumentException">Thrown when the database type is not supported.</exception>
        public static DatabaseDriverBase Create(DatabaseSettings settings)
        {
            if (settings == null)
                throw new ArgumentNullException(nameof(settings));

            switch (settings.Type)
            {
                case DatabaseTypeEnum.Sqlite:
                    return new SqliteDatabaseDriver(settings);
                default:
                    throw new ArgumentException("Unsupported database type: " + settings.Type + ".", nameof(settings));
            }
        }

        /// <summary>
        /// Create a driver for the given settings and initialize it (open the connection and apply
        /// migrations).
        /// </summary>
        /// <param name="settings">Database settings.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>An initialized driver.</returns>
        /// <exception cref="ArgumentNullException">Thrown when <paramref name="settings"/> is null.</exception>
        /// <exception cref="ArgumentException">Thrown when the database type is not supported.</exception>
        public static async Task<DatabaseDriverBase> CreateAndInitializeAsync(DatabaseSettings settings, CancellationToken token = default)
        {
            DatabaseDriverBase driver = Create(settings);
            await driver.InitializeAsync(token).ConfigureAwait(false);
            return driver;
        }
    }
}
