namespace Armor.Core.Service
{
    using System;
    using System.IO;
    using System.Threading;
    using System.Threading.Tasks;
    using Armor.Core.Configuration;
    using Armor.Core.Database;
    using Armor.Core.Security;

    /// <summary>
    /// The shared runtime context for an Armor process: loaded settings, resolved paths, the open
    /// database, and the local credential protector. Both the agent and the TUI build a context to
    /// operate on the same local state. Dispose it to close the database.
    /// </summary>
    public sealed class ArmorContext : IDisposable, IAsyncDisposable
    {
        /// <summary>
        /// The effective configuration.
        /// </summary>
        public ArmorSettings Settings { get; private set; } = null!;

        /// <summary>
        /// The resolved on-disk paths.
        /// </summary>
        public ArmorPaths Paths { get; private set; } = null!;

        /// <summary>
        /// The open database driver.
        /// </summary>
        public DatabaseDriverBase Database { get; private set; } = null!;

        /// <summary>
        /// The credential protector for storage-target secrets.
        /// </summary>
        public CredentialProtector CredentialProtector { get; private set; } = null!;

        private ArmorContext()
        {
        }

        /// <summary>
        /// Create and initialize a context: load settings, open the database, and prepare the
        /// credential protector.
        /// </summary>
        /// <param name="paths">Path resolver. When null, a default <see cref="ArmorPaths"/> is used.</param>
        /// <param name="token">Cancellation token.</param>
        /// <param name="onMaintenance">Optional sink for progress messages emitted during slow one-time
        /// database maintenance at startup (migrations, VACUUM). A startup path can route this to the console
        /// so a large database does not look like a hang while it is being prepared.</param>
        /// <returns>An initialized context.</returns>
        public static async Task<ArmorContext> CreateAsync(ArmorPaths? paths = null, CancellationToken token = default, Action<string>? onMaintenance = null)
        {
            ArmorContext context = new ArmorContext();
            context.Paths = paths ?? new ArmorPaths();

            SettingsManager manager = new SettingsManager(context.Paths);
            context.Settings = await manager.LoadAsync(token).ConfigureAwait(false);

            DatabaseSettings databaseSettings = new DatabaseSettings(context.Settings.DatabaseFilename ?? context.Paths.DefaultDatabasePath);
            databaseSettings.MaintenanceReporter = onMaintenance;
            context.Database = await DatabaseDriverFactory.CreateAndInitializeAsync(databaseSettings, token).ConfigureAwait(false);

            context.CredentialProtector = new CredentialProtector(Path.Combine(context.Paths.StateDirectory, "dp.key"));
            return context;
        }

        /// <summary>
        /// Dispose the context, closing the database.
        /// </summary>
        public void Dispose()
        {
            Database?.Dispose();
        }

        /// <summary>
        /// Asynchronously dispose the context, closing the database.
        /// </summary>
        /// <returns>A value task that completes when disposal is finished.</returns>
        public async ValueTask DisposeAsync()
        {
            if (Database != null)
                await Database.DisposeAsync().ConfigureAwait(false);
        }
    }
}
