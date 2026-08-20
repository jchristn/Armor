namespace Armor.Core.Database.Sqlite.Implementations
{
    using System;
    using System.Collections.Generic;
    using System.Data;
    using System.Threading;
    using System.Threading.Tasks;
    using Armor.Core.Database.Interfaces;
    using Armor.Core.Models;

    /// <summary>
    /// SQLite implementation of <see cref="IPolicyStateMethods"/>. Each policy owns a dedicated table
    /// named <c>policy_state_&lt;policyId&gt;</c>. Read and write operations ensure the table exists so
    /// callers do not have to sequence <see cref="EnsureTableAsync"/> manually.
    /// </summary>
    public sealed class SqlitePolicyStateMethods : IPolicyStateMethods
    {
        private readonly DatabaseDriverBase _Driver;

        /// <summary>
        /// Initializes a new instance of the <see cref="SqlitePolicyStateMethods"/> class.
        /// </summary>
        /// <param name="driver">The owning database driver. Cannot be null.</param>
        /// <exception cref="ArgumentNullException">Thrown when <paramref name="driver"/> is null.</exception>
        public SqlitePolicyStateMethods(DatabaseDriverBase driver)
        {
            _Driver = driver ?? throw new ArgumentNullException(nameof(driver));
        }

        /// <inheritdoc/>
        public async Task EnsureTableAsync(string policyId, CancellationToken token = default)
        {
            string table = TableName(policyId);
            await _Driver.ExecuteQueryAsync(
                "CREATE TABLE IF NOT EXISTS " + table + " (" +
                "path TEXT PRIMARY KEY, " +
                "size_bytes INTEGER NOT NULL DEFAULT 0, " +
                "modified_utc TEXT NOT NULL, " +
                "archive_bit INTEGER NOT NULL DEFAULT 0, " +
                "chunk_list_hash TEXT NULL, " +
                "last_job_id TEXT NULL, " +
                "updated_utc TEXT NOT NULL);", false, token).ConfigureAwait(false);
        }

        /// <inheritdoc/>
        public async Task DropTableAsync(string policyId, CancellationToken token = default)
        {
            string table = TableName(policyId);
            await _Driver.ExecuteQueryAsync("DROP TABLE IF EXISTS " + table + ";", false, token).ConfigureAwait(false);
        }

        /// <inheritdoc/>
        public async Task<PolicyStateEntry?> ReadAsync(string policyId, string path, CancellationToken token = default)
        {
            if (String.IsNullOrWhiteSpace(path))
                throw new ArgumentNullException(nameof(path));

            string table = TableName(policyId);
            await EnsureTableAsync(policyId, token).ConfigureAwait(false);

            DataTable result = await _Driver.ExecuteQueryAsync(
                "SELECT * FROM " + table + " WHERE path = " + Sanitizer.Literal(path) + ";", false, token).ConfigureAwait(false);

            return result.Rows.Count == 0 ? null : MapRow(result.Rows[0]);
        }

        /// <inheritdoc/>
        public async Task<List<PolicyStateEntry>> ReadAllAsync(string policyId, CancellationToken token = default)
        {
            string table = TableName(policyId);
            await EnsureTableAsync(policyId, token).ConfigureAwait(false);

            DataTable result = await _Driver.ExecuteQueryAsync(
                "SELECT * FROM " + table + " ORDER BY path ASC;", false, token).ConfigureAwait(false);

            List<PolicyStateEntry> list = new List<PolicyStateEntry>();
            foreach (DataRow row in result.Rows)
                list.Add(MapRow(row));
            return list;
        }

        /// <inheritdoc/>
        public async Task UpsertAsync(string policyId, PolicyStateEntry entry, CancellationToken token = default)
        {
            if (entry == null)
                throw new ArgumentNullException(nameof(entry));

            string table = TableName(policyId);
            await EnsureTableAsync(policyId, token).ConfigureAwait(false);

            await _Driver.ExecuteQueryAsync(
                "INSERT INTO " + table + " (path, size_bytes, modified_utc, archive_bit, chunk_list_hash, last_job_id, updated_utc) VALUES (" +
                Sanitizer.Literal(entry.Path) + ", " +
                Sanitizer.Int(entry.SizeBytes) + ", " +
                Sanitizer.Timestamp(entry.ModifiedUtc) + ", " +
                Sanitizer.Bool(entry.ArchiveBit) + ", " +
                Sanitizer.Quote(entry.ChunkListHash) + ", " +
                Sanitizer.Quote(entry.LastJobId) + ", " +
                Sanitizer.Timestamp(entry.UpdatedUtc) + ") " +
                "ON CONFLICT(path) DO UPDATE SET " +
                "size_bytes = excluded.size_bytes, " +
                "modified_utc = excluded.modified_utc, " +
                "archive_bit = excluded.archive_bit, " +
                "chunk_list_hash = excluded.chunk_list_hash, " +
                "last_job_id = excluded.last_job_id, " +
                "updated_utc = excluded.updated_utc;", false, token).ConfigureAwait(false);
        }

        /// <inheritdoc/>
        public async Task<bool> DeleteAsync(string policyId, string path, CancellationToken token = default)
        {
            if (String.IsNullOrWhiteSpace(path))
                throw new ArgumentNullException(nameof(path));

            string table = TableName(policyId);
            await EnsureTableAsync(policyId, token).ConfigureAwait(false);

            DataTable existing = await _Driver.ExecuteQueryAsync(
                "SELECT COUNT(*) AS count FROM " + table + " WHERE path = " + Sanitizer.Literal(path) + ";", false, token).ConfigureAwait(false);
            if (existing.Rows.Count == 0 || Converters.GetLong(existing.Rows[0], "count") == 0)
                return false;

            await _Driver.ExecuteQueryAsync(
                "DELETE FROM " + table + " WHERE path = " + Sanitizer.Literal(path) + ";", false, token).ConfigureAwait(false);
            return true;
        }

        private static string TableName(string policyId)
        {
            if (String.IsNullOrWhiteSpace(policyId))
                throw new ArgumentNullException(nameof(policyId));
            return "policy_state_" + Sanitizer.Identifier(policyId);
        }

        private static PolicyStateEntry MapRow(DataRow row)
        {
            PolicyStateEntry entry = new PolicyStateEntry();
            entry.Path = Converters.GetString(row, "path");
            entry.SizeBytes = Converters.GetLong(row, "size_bytes");
            entry.ModifiedUtc = Converters.GetDateTime(row, "modified_utc");
            entry.ArchiveBit = Converters.GetBool(row, "archive_bit");
            entry.ChunkListHash = Converters.GetStringOrNull(row, "chunk_list_hash");
            entry.LastJobId = Converters.GetStringOrNull(row, "last_job_id");
            entry.UpdatedUtc = Converters.GetDateTime(row, "updated_utc");
            return entry;
        }
    }
}
