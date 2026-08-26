namespace Armor.Core.Database
{
    using System;
    using System.Collections.Generic;
    using System.Data;
    using System.Threading;
    using System.Threading.Tasks;
    using Armor.Core.Database.Interfaces;

    /// <summary>
    /// Provider-neutral base for Armor database drivers. Concrete drivers implement the low-level
    /// query methods and wire up the domain method interfaces. Drivers own a database connection and
    /// must be disposed. Instances are thread-safe: implementations serialize writes internally.
    /// </summary>
    public abstract class DatabaseDriverBase : IDisposable, IAsyncDisposable
    {
        /// <summary>
        /// The provider type of this driver.
        /// </summary>
        public abstract DatabaseTypeEnum DatabaseType { get; }

        /// <summary>
        /// Policy data-access methods.
        /// </summary>
        public IPolicyMethods Policies { get; protected set; } = null!;

        /// <summary>
        /// Schedule data-access methods.
        /// </summary>
        public IScheduleMethods Schedules { get; protected set; } = null!;

        /// <summary>
        /// Storage-target data-access methods.
        /// </summary>
        public IStorageTargetMethods StorageTargets { get; protected set; } = null!;

        /// <summary>
        /// Encryption-key data-access methods.
        /// </summary>
        public IEncryptionKeyMethods EncryptionKeys { get; protected set; } = null!;

        /// <summary>
        /// Backup-job data-access methods.
        /// </summary>
        public IBackupJobMethods BackupJobs { get; protected set; } = null!;

        /// <summary>
        /// Restore-job data-access methods.
        /// </summary>
        public IRestoreJobMethods RestoreJobs { get; protected set; } = null!;

        /// <summary>
        /// Chunk-index data-access methods.
        /// </summary>
        public IChunkIndexMethods ChunkIndex { get; protected set; } = null!;

        /// <summary>
        /// Per-policy state-table data-access methods.
        /// </summary>
        public IPolicyStateMethods PolicyState { get; protected set; } = null!;

        /// <summary>
        /// Per-job work-list (<c>job_files</c>) data-access methods, used to stream a backup's manifest to
        /// disk and to resume a failed run.
        /// </summary>
        public IJobFileMethods JobFiles { get; protected set; } = null!;

        /// <summary>
        /// Open the database, apply pending migrations, and prepare the driver for use. Idempotent.
        /// </summary>
        /// <param name="token">Cancellation token.</param>
        /// <returns>A task that completes when the driver is ready.</returns>
        public abstract Task InitializeAsync(CancellationToken token = default);

        /// <summary>
        /// Execute a single SQL statement and return the resulting rows.
        /// </summary>
        /// <param name="query">The SQL statement.</param>
        /// <param name="isTransaction">When true, the statement runs within a transaction.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>The result set as a <see cref="DataTable"/>.</returns>
        public abstract Task<DataTable> ExecuteQueryAsync(string query, bool isTransaction = false, CancellationToken token = default);

        /// <summary>
        /// Execute several SQL statements, optionally within a single transaction.
        /// </summary>
        /// <param name="queries">The SQL statements, executed in order.</param>
        /// <param name="isTransaction">When true, all statements run within one transaction.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>The result set of the final statement as a <see cref="DataTable"/>.</returns>
        public abstract Task<DataTable> ExecuteQueriesAsync(IEnumerable<string> queries, bool isTransaction = false, CancellationToken token = default);

        /// <summary>
        /// Close the database connection.
        /// </summary>
        /// <param name="token">Cancellation token.</param>
        /// <returns>A task that completes when the connection is closed.</returns>
        public abstract Task CloseAsync(CancellationToken token = default);

        /// <summary>
        /// Release resources held by the driver.
        /// </summary>
        public void Dispose()
        {
            Dispose(true);
            GC.SuppressFinalize(this);
        }

        /// <summary>
        /// Asynchronously release resources held by the driver.
        /// </summary>
        /// <returns>A value task that completes when disposal is finished.</returns>
        public async ValueTask DisposeAsync()
        {
            await DisposeAsyncCore().ConfigureAwait(false);
            Dispose(false);
            GC.SuppressFinalize(this);
        }

        /// <summary>
        /// Release managed and unmanaged resources.
        /// </summary>
        /// <param name="disposing">True when called from <see cref="Dispose()"/>; false from the finalizer or async disposal.</param>
        protected virtual void Dispose(bool disposing)
        {
        }

        /// <summary>
        /// Asynchronously release resources. Derived types override to close connections without
        /// blocking.
        /// </summary>
        /// <returns>A value task that completes when disposal is finished.</returns>
        protected virtual ValueTask DisposeAsyncCore()
        {
            return default;
        }
    }
}
