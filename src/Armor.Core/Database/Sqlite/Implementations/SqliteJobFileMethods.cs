namespace Armor.Core.Database.Sqlite.Implementations
{
    using System;
    using System.Collections.Generic;
    using System.Data;
    using System.Threading;
    using System.Threading.Tasks;
    using Armor.Core.Database.Interfaces;
    using Armor.Core.Models;
    using Armor.Core.Serialization;

    /// <summary>
    /// SQLite implementation of <see cref="IJobFileMethods"/> over the shared <c>job_files</c> table
    /// (created by the schema migrations). Rows are addressed by their integer rowid (<c>id</c>); pending
    /// rows are read in rowid-ordered pages so a run never holds its whole file list in memory.
    /// </summary>
    public sealed class SqliteJobFileMethods : IJobFileMethods
    {
        private readonly DatabaseDriverBase _Driver;

        /// <summary>
        /// Initializes a new instance of the <see cref="SqliteJobFileMethods"/> class.
        /// </summary>
        /// <param name="driver">The owning database driver. Cannot be null.</param>
        /// <exception cref="ArgumentNullException">Thrown when <paramref name="driver"/> is null.</exception>
        public SqliteJobFileMethods(DatabaseDriverBase driver)
        {
            _Driver = driver ?? throw new ArgumentNullException(nameof(driver));
        }

        /// <inheritdoc/>
        public async Task AddPendingAsync(string jobId, IReadOnlyList<JobFileEntry> entries, CancellationToken token = default)
        {
            RequireJobId(jobId);
            if (entries == null)
                throw new ArgumentNullException(nameof(entries));
            if (entries.Count == 0)
                return;

            List<string> statements = new List<string>(entries.Count);
            foreach (JobFileEntry entry in entries)
            {
                statements.Add(
                    "INSERT INTO job_files (job_id, path, size_bytes, modified_utc, archive_bit, done, chunk_hashes) VALUES (" +
                    Sanitizer.Literal(jobId) + ", " +
                    Sanitizer.Literal(entry.Path) + ", " +
                    Sanitizer.Int(entry.SizeBytes) + ", " +
                    Sanitizer.Timestamp(entry.ModifiedUtc) + ", " +
                    Sanitizer.Bool(entry.ArchiveBit) + ", 0, NULL);");
            }

            await _Driver.ExecuteQueriesAsync(statements, true, token).ConfigureAwait(false);
        }

        /// <inheritdoc/>
        public async Task<JobFileTotals> ReadTotalsAsync(string jobId, CancellationToken token = default)
        {
            RequireJobId(jobId);

            DataTable result = await _Driver.ExecuteQueryAsync(
                "SELECT COUNT(*) AS file_count, " +
                "COALESCE(SUM(size_bytes), 0) AS total_bytes, " +
                "COALESCE(SUM(CASE WHEN done = 1 THEN 1 ELSE 0 END), 0) AS done_count, " +
                "COALESCE(SUM(CASE WHEN done = 1 THEN size_bytes ELSE 0 END), 0) AS done_bytes " +
                "FROM job_files WHERE job_id = " + Sanitizer.Literal(jobId) + ";", false, token).ConfigureAwait(false);

            JobFileTotals totals = new JobFileTotals();
            if (result.Rows.Count > 0)
            {
                totals.FileCount = (int)Converters.GetLong(result.Rows[0], "file_count");
                totals.TotalBytes = Converters.GetLong(result.Rows[0], "total_bytes");
                totals.DoneCount = (int)Converters.GetLong(result.Rows[0], "done_count");
                totals.DoneBytes = Converters.GetLong(result.Rows[0], "done_bytes");
            }
            return totals;
        }

        /// <inheritdoc/>
        public async Task MarkDoneAsync(long rowid, long sizeBytes, DateTime modifiedUtc, bool archiveBit, string chunkHashesJson, CancellationToken token = default)
        {
            await _Driver.ExecuteQueryAsync(
                "UPDATE job_files SET done = 1, " +
                "size_bytes = " + Sanitizer.Int(sizeBytes) + ", " +
                "modified_utc = " + Sanitizer.Timestamp(modifiedUtc) + ", " +
                "archive_bit = " + Sanitizer.Bool(archiveBit) + ", " +
                "chunk_hashes = " + Sanitizer.Quote(chunkHashesJson) + " " +
                "WHERE id = " + Sanitizer.Int(rowid) + ";", false, token).ConfigureAwait(false);
        }

        /// <inheritdoc/>
        public async Task RemoveAsync(long rowid, CancellationToken token = default)
        {
            await _Driver.ExecuteQueryAsync(
                "DELETE FROM job_files WHERE id = " + Sanitizer.Int(rowid) + ";", false, token).ConfigureAwait(false);
        }

        /// <inheritdoc/>
        public async Task<List<JobFileEntry>> ReadPendingPageAsync(string jobId, int limit, CancellationToken token = default)
        {
            RequireJobId(jobId);
            int take = limit < 1 ? 1 : limit;

            DataTable result = await _Driver.ExecuteQueryAsync(
                "SELECT * FROM job_files WHERE job_id = " + Sanitizer.Literal(jobId) +
                " AND done = 0 ORDER BY id ASC LIMIT " + Sanitizer.Int(take) + ";", false, token).ConfigureAwait(false);

            return MapRows(result);
        }

        /// <inheritdoc/>
        public async Task<List<JobFileEntry>> ReadPendingPageAsync(string jobId, long afterRowid, int limit, CancellationToken token = default)
        {
            RequireJobId(jobId);
            int take = limit < 1 ? 1 : limit;

            DataTable result = await _Driver.ExecuteQueryAsync(
                "SELECT * FROM job_files WHERE job_id = " + Sanitizer.Literal(jobId) +
                " AND done = 0 AND id > " + Sanitizer.Int(afterRowid) +
                " ORDER BY id ASC LIMIT " + Sanitizer.Int(take) + ";", false, token).ConfigureAwait(false);

            return MapRows(result);
        }

        /// <inheritdoc/>
        public async Task<List<JobFileEntry>> ReadDonePageAsync(string jobId, long afterRowid, int limit, CancellationToken token = default)
        {
            RequireJobId(jobId);
            int take = limit < 1 ? 1 : limit;

            DataTable result = await _Driver.ExecuteQueryAsync(
                "SELECT * FROM job_files WHERE job_id = " + Sanitizer.Literal(jobId) +
                " AND done = 1 AND id > " + Sanitizer.Int(afterRowid) +
                " ORDER BY id ASC LIMIT " + Sanitizer.Int(take) + ";", false, token).ConfigureAwait(false);

            return MapRows(result);
        }

        /// <inheritdoc/>
        public async Task<bool> HasPendingAsync(string jobId, CancellationToken token = default)
        {
            RequireJobId(jobId);

            DataTable result = await _Driver.ExecuteQueryAsync(
                "SELECT COUNT(*) AS count FROM (SELECT 1 FROM job_files WHERE job_id = " + Sanitizer.Literal(jobId) +
                " AND done = 0 LIMIT 1);", false, token).ConfigureAwait(false);

            return result.Rows.Count > 0 && Converters.GetLong(result.Rows[0], "count") > 0;
        }

        /// <inheritdoc/>
        public async Task DeleteByJobAsync(string jobId, CancellationToken token = default)
        {
            RequireJobId(jobId);

            // Delete in bounded batches rather than one statement. The whole database is served by a single
            // connection behind one lock, so a single "DELETE … WHERE job_id = X" over a work list of
            // millions of rows would hold that lock for the entire delete — freezing every other database
            // user, including the TUI trying to read a policy to edit it. Each bounded batch releases the
            // lock between iterations so interactive reads can interleave. A yield between batches keeps the
            // scheduler fair.
            const int batchSize = 5000;
            while (true)
            {
                token.ThrowIfCancellationRequested();

                await _Driver.ExecuteQueryAsync(
                    "DELETE FROM job_files WHERE id IN (SELECT id FROM job_files WHERE job_id = " +
                    Sanitizer.Literal(jobId) + " LIMIT " + Sanitizer.Int(batchSize) + ");", false, token).ConfigureAwait(false);

                DataTable remaining = await _Driver.ExecuteQueryAsync(
                    "SELECT EXISTS(SELECT 1 FROM job_files WHERE job_id = " + Sanitizer.Literal(jobId) + ") AS more;", false, token).ConfigureAwait(false);
                if (remaining.Rows.Count == 0 || Converters.GetLong(remaining.Rows[0], "more") == 0)
                    break;

                await Task.Yield();
            }
        }

        private static void RequireJobId(string jobId)
        {
            if (String.IsNullOrWhiteSpace(jobId))
                throw new ArgumentNullException(nameof(jobId));
        }

        private static List<JobFileEntry> MapRows(DataTable result)
        {
            List<JobFileEntry> list = new List<JobFileEntry>();
            foreach (DataRow row in result.Rows)
                list.Add(MapRow(row));
            return list;
        }

        private static JobFileEntry MapRow(DataRow row)
        {
            JobFileEntry entry = new JobFileEntry();
            entry.Rowid = Converters.GetLong(row, "id");
            entry.Path = Converters.GetString(row, "path");
            entry.SizeBytes = Converters.GetLong(row, "size_bytes");
            entry.ModifiedUtc = Converters.GetDateTime(row, "modified_utc");
            entry.ArchiveBit = Converters.GetBool(row, "archive_bit");
            entry.Done = Converters.GetBool(row, "done");

            string? json = Converters.GetStringOrNull(row, "chunk_hashes");
            if (!String.IsNullOrEmpty(json))
            {
                List<string>? hashes = ArmorJson.Deserialize<List<string>>(json!);
                if (hashes != null)
                    entry.ChunkHashes = hashes;
            }
            return entry;
        }
    }
}
