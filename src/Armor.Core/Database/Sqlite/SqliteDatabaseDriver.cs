namespace Armor.Core.Database.Sqlite
{
    using System;
    using System.Collections.Generic;
    using System.Data;
    using System.IO;
    using System.Threading;
    using System.Threading.Tasks;
    using Armor.Core.Database.Sqlite.Implementations;
    using Microsoft.Data.Sqlite;

    /// <summary>
    /// SQLite implementation of <see cref="DatabaseDriverBase"/>. A single connection is held for the
    /// driver's lifetime and all access is serialized with a semaphore, so the connection is used by
    /// one operation at a time. The database opens in WAL mode with a busy timeout so a second Armor
    /// process (the agent or the TUI) can read and wait rather than fail. This type is thread-safe.
    /// </summary>
    public sealed class SqliteDatabaseDriver : DatabaseDriverBase
    {
        private readonly DatabaseSettings _Settings;
        private readonly SemaphoreSlim _Semaphore = new SemaphoreSlim(1, 1);
        private SqliteConnection? _Connection;
        private bool _Initialized;
        private bool _Disposed;

        /// <inheritdoc/>
        public override DatabaseTypeEnum DatabaseType
        {
            get { return DatabaseTypeEnum.Sqlite; }
        }

        /// <summary>
        /// Initializes a new instance of the <see cref="SqliteDatabaseDriver"/> class.
        /// </summary>
        /// <param name="settings">Database settings. Cannot be null.</param>
        /// <exception cref="ArgumentNullException">Thrown when <paramref name="settings"/> is null.</exception>
        public SqliteDatabaseDriver(DatabaseSettings settings)
        {
            if (settings == null)
                throw new ArgumentNullException(nameof(settings));

            _Settings = settings;

            Policies = new SqlitePolicyMethods(this);
            Schedules = new SqliteScheduleMethods(this);
            StorageTargets = new SqliteStorageTargetMethods(this);
            EncryptionKeys = new SqliteEncryptionKeyMethods(this);
            BackupJobs = new SqliteBackupJobMethods(this);
            RestoreJobs = new SqliteRestoreJobMethods(this);
            ChunkIndex = new SqliteChunkIndexMethods(this);
            PolicyState = new SqlitePolicyStateMethods(this);
        }

        /// <inheritdoc/>
        public override async Task InitializeAsync(CancellationToken token = default)
        {
            ThrowIfDisposed();

            await _Semaphore.WaitAsync(token).ConfigureAwait(false);
            try
            {
                if (_Initialized)
                    return;

                string? directory = Path.GetDirectoryName(Path.GetFullPath(_Settings.Filename));
                if (!String.IsNullOrEmpty(directory))
                    Directory.CreateDirectory(directory);

                SqliteConnectionStringBuilder builder = new SqliteConnectionStringBuilder
                {
                    DataSource = _Settings.Filename
                };

                _Connection = new SqliteConnection(builder.ToString());
                await _Connection.OpenAsync(token).ConfigureAwait(false);

                await ExecuteNoLockAsync("PRAGMA journal_mode=WAL;", null, token).ConfigureAwait(false);
                await ExecuteNoLockAsync("PRAGMA busy_timeout=" + _Settings.BusyTimeoutMilliseconds + ";", null, token).ConfigureAwait(false);
                await ExecuteNoLockAsync("PRAGMA foreign_keys=ON;", null, token).ConfigureAwait(false);

                await ApplyMigrationsAsync(token).ConfigureAwait(false);

                _Initialized = true;
            }
            finally
            {
                _Semaphore.Release();
            }
        }

        /// <inheritdoc/>
        public override async Task<DataTable> ExecuteQueryAsync(string query, bool isTransaction = false, CancellationToken token = default)
        {
            if (String.IsNullOrWhiteSpace(query))
                throw new ArgumentNullException(nameof(query));

            ThrowIfDisposed();

            await _Semaphore.WaitAsync(token).ConfigureAwait(false);
            try
            {
                if (!isTransaction)
                    return await ExecuteNoLockAsync(query, null, token).ConfigureAwait(false);

                return await ExecuteManyNoLockAsync(new List<string> { query }, token).ConfigureAwait(false);
            }
            finally
            {
                _Semaphore.Release();
            }
        }

        /// <inheritdoc/>
        public override async Task<DataTable> ExecuteQueriesAsync(IEnumerable<string> queries, bool isTransaction = false, CancellationToken token = default)
        {
            if (queries == null)
                throw new ArgumentNullException(nameof(queries));

            ThrowIfDisposed();

            List<string> list = new List<string>(queries);

            await _Semaphore.WaitAsync(token).ConfigureAwait(false);
            try
            {
                if (isTransaction)
                    return await ExecuteManyNoLockAsync(list, token).ConfigureAwait(false);

                DataTable last = new DataTable();
                foreach (string query in list)
                {
                    token.ThrowIfCancellationRequested();
                    last = await ExecuteNoLockAsync(query, null, token).ConfigureAwait(false);
                }
                return last;
            }
            finally
            {
                _Semaphore.Release();
            }
        }

        /// <inheritdoc/>
        public override async Task CloseAsync(CancellationToken token = default)
        {
            await _Semaphore.WaitAsync(token).ConfigureAwait(false);
            try
            {
                if (_Connection != null)
                {
                    await _Connection.CloseAsync().ConfigureAwait(false);
                    await _Connection.DisposeAsync().ConfigureAwait(false);
                    _Connection = null;
                }
                _Initialized = false;
            }
            finally
            {
                _Semaphore.Release();
            }
        }

        /// <inheritdoc/>
        protected override void Dispose(bool disposing)
        {
            if (_Disposed)
                return;

            if (disposing)
            {
                if (_Connection != null)
                {
                    _Connection.Dispose();
                    _Connection = null;
                }
                _Semaphore.Dispose();
            }

            _Disposed = true;
            base.Dispose(disposing);
        }

        /// <inheritdoc/>
        protected override async ValueTask DisposeAsyncCore()
        {
            if (_Disposed)
                return;

            if (_Connection != null)
            {
                await _Connection.DisposeAsync().ConfigureAwait(false);
                _Connection = null;
            }
        }

        private async Task<DataTable> ExecuteManyNoLockAsync(List<string> queries, CancellationToken token)
        {
            if (_Connection == null)
                throw new InvalidOperationException("Database driver is not initialized.");

            DataTable last = new DataTable();

            using (SqliteTransaction transaction = (SqliteTransaction)await _Connection.BeginTransactionAsync(token).ConfigureAwait(false))
            {
                try
                {
                    foreach (string query in queries)
                    {
                        token.ThrowIfCancellationRequested();
                        last = await ExecuteNoLockAsync(query, transaction, token).ConfigureAwait(false);
                    }

                    await transaction.CommitAsync(token).ConfigureAwait(false);
                }
                catch
                {
                    await transaction.RollbackAsync(CancellationToken.None).ConfigureAwait(false);
                    throw;
                }
            }

            return last;
        }

        private async Task<DataTable> ExecuteNoLockAsync(string query, SqliteTransaction? transaction, CancellationToken token)
        {
            if (_Connection == null)
                throw new InvalidOperationException("Database driver is not initialized.");

            DataTable table = new DataTable();

            using (SqliteCommand command = _Connection.CreateCommand())
            {
                command.CommandText = query;
                if (transaction != null)
                    command.Transaction = transaction;

                using (SqliteDataReader reader = (SqliteDataReader)await command.ExecuteReaderAsync(token).ConfigureAwait(false))
                {
                    int fieldCount = reader.FieldCount;
                    for (int i = 0; i < fieldCount; i++)
                        table.Columns.Add(reader.GetName(i), typeof(object));

                    while (await reader.ReadAsync(token).ConfigureAwait(false))
                    {
                        DataRow row = table.NewRow();
                        for (int i = 0; i < fieldCount; i++)
                            row[i] = reader.IsDBNull(i) ? DBNull.Value : reader.GetValue(i);
                        table.Rows.Add(row);
                    }
                }
            }

            return table;
        }

        private async Task ApplyMigrationsAsync(CancellationToken token)
        {
            await ExecuteNoLockAsync(
                "CREATE TABLE IF NOT EXISTS schema_migrations (version INTEGER PRIMARY KEY, description TEXT NOT NULL, applied_utc TEXT NOT NULL);",
                null,
                token).ConfigureAwait(false);

            DataTable applied = await ExecuteNoLockAsync("SELECT COALESCE(MAX(version), 0) AS max_version FROM schema_migrations;", null, token).ConfigureAwait(false);
            long maxApplied = 0;
            if (applied.Rows.Count > 0)
                maxApplied = Converters.GetLong(applied.Rows[0], "max_version");

            List<SchemaMigration> migrations = SqliteMigrations.All();
            foreach (SchemaMigration migration in migrations)
            {
                if (migration.Version <= maxApplied)
                    continue;

                token.ThrowIfCancellationRequested();

                using (SqliteTransaction transaction = (SqliteTransaction)await _Connection!.BeginTransactionAsync(token).ConfigureAwait(false))
                {
                    try
                    {
                        foreach (string statement in migration.Statements)
                            await ExecuteNoLockAsync(statement, transaction, token).ConfigureAwait(false);

                        await ExecuteNoLockAsync(
                            "INSERT INTO schema_migrations (version, description, applied_utc) VALUES (" +
                            Sanitizer.Int(migration.Version) + ", " +
                            Sanitizer.Literal(migration.Description) + ", " +
                            Sanitizer.Timestamp(DateTime.UtcNow) + ");",
                            transaction,
                            token).ConfigureAwait(false);

                        await transaction.CommitAsync(token).ConfigureAwait(false);
                    }
                    catch
                    {
                        await transaction.RollbackAsync(CancellationToken.None).ConfigureAwait(false);
                        throw;
                    }
                }
            }
        }

        private void ThrowIfDisposed()
        {
            if (_Disposed)
                throw new ObjectDisposedException(nameof(SqliteDatabaseDriver));
        }
    }
}
