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
            JobFiles = new SqliteJobFileMethods(this);
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

                // Throughput tuning. A backup writes millions of small chunk-index rows; with the default
                // synchronous=FULL every autocommit fsyncs, which is the dominant cost on a fast disk. NORMAL
                // still guarantees no corruption in WAL mode — a crash can lose only the last transaction or
                // two, which a resumed backup simply re-does (chunk blobs are written before their index rows,
                // so nothing referenced is ever lost). A large page cache and in-memory temporaries keep the
                // hot path off the disk; a higher WAL autocheckpoint lets writes batch before a checkpoint.
                await ExecuteNoLockAsync("PRAGMA synchronous=NORMAL;", null, token).ConfigureAwait(false);
                await ExecuteNoLockAsync("PRAGMA temp_store=MEMORY;", null, token).ConfigureAwait(false);
                await ExecuteNoLockAsync("PRAGMA cache_size=-65536;", null, token).ConfigureAwait(false);
                await ExecuteNoLockAsync("PRAGMA wal_autocheckpoint=4000;", null, token).ConfigureAwait(false);

                await ApplyMigrationsAsync(token).ConfigureAwait(false);
                await ReclaimIfBloatedAsync(token).ConfigureAwait(false);

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

                string label = "Applying database migration " + migration.Version + " (" + migration.Description + ")";
                Report(label + "…");

                await WithHeartbeatAsync(label, async () =>
                {
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
                    return true;
                }).ConfigureAwait(false);
            }
        }

        /// <summary>
        /// Reclaim free pages when the database file has become badly over-allocated. Dropping or rebuilding
        /// a large table (for example the multi-gigabyte <c>job_files</c> work list left by an interrupted
        /// run, or the compaction migration that replaces it) moves its pages onto the freelist but does not
        /// shrink the file. A full <c>VACUUM</c> rewrites the database and returns that space to the OS. It
        /// cannot run inside a transaction, so it executes here on its own after the migrations commit. The
        /// work is gated on the freelist size so a healthy database is never rewritten on startup.
        /// </summary>
        private async Task ReclaimIfBloatedAsync(CancellationToken token)
        {
            DataTable freeTable = await ExecuteNoLockAsync("PRAGMA freelist_count;", null, token).ConfigureAwait(false);
            DataTable pageSizeTable = await ExecuteNoLockAsync("PRAGMA page_size;", null, token).ConfigureAwait(false);
            DataTable pageCountTable = await ExecuteNoLockAsync("PRAGMA page_count;", null, token).ConfigureAwait(false);
            if (freeTable.Rows.Count == 0 || pageSizeTable.Rows.Count == 0 || pageCountTable.Rows.Count == 0)
                return;

            long freePages = Converters.GetLong(freeTable.Rows[0], "freelist_count");
            long pageSize = Converters.GetLong(pageSizeTable.Rows[0], "page_size");
            long pageCount = Converters.GetLong(pageCountTable.Rows[0], "page_count");
            long freeBytes = freePages * pageSize;
            long liveBytes = Math.Max(0, (pageCount - freePages) * pageSize);

            // 256 MiB of dead space is the threshold: below it a VACUUM is not worth the whole-file rewrite;
            // above it the file is carrying a dropped work list (which can be many gigabytes) that should go
            // back to the OS.
            const long reclaimThresholdBytes = 256L * 1024 * 1024;
            if (freeBytes < reclaimThresholdBytes)
                return;

            // VACUUM builds a fresh copy of the live data before swapping it in, so it needs free disk space
            // roughly equal to the live (not the free) bytes, plus a margin. On a nearly-full disk a VACUUM
            // could fail partway; skip it and leave the free pages to be reused in place rather than risk
            // filling the volume. The file stays large but the database is fully usable.
            const long marginBytes = 512L * 1024 * 1024;
            long requiredBytes = liveBytes + marginBytes;
            long availableBytes = TryGetAvailableFreeBytes(_Settings.Filename);
            if (availableBytes >= 0 && availableBytes < requiredBytes)
            {
                string skip = "Skipping database compaction: reclaiming " + FormatMb(freeBytes) +
                    " needs about " + FormatMb(requiredBytes) + " free on the drive but only " +
                    FormatMb(availableBytes) + " is available. Free up space and restart to reclaim it.";
                Report(skip);
                Diagnostics.ArmorLog.Warn(skip);
                return;
            }

            string start = "Reclaiming " + FormatMb(freeBytes) + " of free database space (VACUUM); this can take a while on a large database.";
            Report(start);
            Diagnostics.ArmorLog.Info(start);
            await WithHeartbeatAsync("Reclaiming database space", () => ExecuteNoLockAsync("VACUUM;", null, token)).ConfigureAwait(false);
            Report("Database compaction complete.");
            Diagnostics.ArmorLog.Info("Database VACUUM complete.");
        }

        private void Report(string message)
        {
            Action<string>? reporter = _Settings.MaintenanceReporter;
            if (reporter != null)
            {
                try
                {
                    reporter(message);
                }
                catch
                {
                    // A reporter is best-effort console feedback; never let it break startup.
                }
            }
        }

        /// <summary>
        /// Run a potentially slow operation while emitting a periodic "still working" heartbeat through the
        /// maintenance reporter, so a multi-minute migration or VACUUM does not look like a hang. The first
        /// tick only fires after a few seconds, so quick work stays silent.
        /// </summary>
        private async Task<T> WithHeartbeatAsync<T>(string label, Func<Task<T>> work)
        {
            using (CancellationTokenSource beat = new CancellationTokenSource())
            {
                Task ticker = HeartbeatAsync(label, beat.Token);
                try
                {
                    return await work().ConfigureAwait(false);
                }
                finally
                {
                    beat.Cancel();
                    try
                    {
                        await ticker.ConfigureAwait(false);
                    }
                    catch (OperationCanceledException)
                    {
                    }
                }
            }
        }

        private async Task HeartbeatAsync(string label, CancellationToken token)
        {
            int seconds = 0;
            while (true)
            {
                try
                {
                    await Task.Delay(3000, token).ConfigureAwait(false);
                }
                catch (OperationCanceledException)
                {
                    return;
                }
                seconds += 3;
                Report(label + " — still working (" + seconds + "s elapsed)");
            }
        }

        private static long TryGetAvailableFreeBytes(string path)
        {
            try
            {
                string full = Path.GetFullPath(path);
                string? root = Path.GetPathRoot(full);
                if (String.IsNullOrEmpty(root))
                    return -1;
                DriveInfo drive = new DriveInfo(root);
                return drive.AvailableFreeSpace;
            }
            catch
            {
                return -1;
            }
        }

        private static string FormatMb(long bytes)
        {
            if (bytes >= 1024L * 1024 * 1024)
                return (bytes / (1024.0 * 1024 * 1024)).ToString("0.0") + " GB";
            return (bytes / (1024 * 1024)) + " MB";
        }

        private void ThrowIfDisposed()
        {
            if (_Disposed)
                throw new ObjectDisposedException(nameof(SqliteDatabaseDriver));
        }
    }
}
