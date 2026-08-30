namespace Armor.Core.Database.Sqlite.Implementations
{
    using System;
    using System.Collections.Generic;
    using System.Data;
    using System.Threading;
    using System.Threading.Tasks;
    using Armor.Core.Database.Interfaces;
    using Armor.Core.Enums;
    using Armor.Core.Helpers;
    using Armor.Core.Models;

    /// <summary>
    /// SQLite implementation of <see cref="IBackupJobMethods"/>.
    /// </summary>
    public sealed class SqliteBackupJobMethods : IBackupJobMethods
    {
        private readonly DatabaseDriverBase _Driver;

        /// <summary>
        /// Initializes a new instance of the <see cref="SqliteBackupJobMethods"/> class.
        /// </summary>
        /// <param name="driver">The owning database driver. Cannot be null.</param>
        /// <exception cref="ArgumentNullException">Thrown when <paramref name="driver"/> is null.</exception>
        public SqliteBackupJobMethods(DatabaseDriverBase driver)
        {
            _Driver = driver ?? throw new ArgumentNullException(nameof(driver));
        }

        /// <inheritdoc/>
        public async Task<BackupJob> CreateAsync(BackupJob job, CancellationToken token = default)
        {
            if (job == null)
                throw new ArgumentNullException(nameof(job));
            if (String.IsNullOrWhiteSpace(job.Id))
                job.Id = IdGenerator.GenerateBackupJobId();

            await _Driver.ExecuteQueryAsync(BuildUpsert(job, true), false, token).ConfigureAwait(false);
            return job;
        }

        /// <inheritdoc/>
        public async Task<BackupJob?> ReadAsync(string id, CancellationToken token = default)
        {
            if (String.IsNullOrWhiteSpace(id))
                throw new ArgumentNullException(nameof(id));

            DataTable table = await _Driver.ExecuteQueryAsync(
                "SELECT * FROM backup_jobs WHERE id = " + Sanitizer.Literal(id) + ";", false, token).ConfigureAwait(false);

            return table.Rows.Count == 0 ? null : MapRow(table.Rows[0]);
        }

        /// <inheritdoc/>
        public async Task<List<BackupJob>> ReadAllAsync(CancellationToken token = default)
        {
            DataTable table = await _Driver.ExecuteQueryAsync(
                "SELECT * FROM backup_jobs ORDER BY created_utc ASC, id ASC;", false, token).ConfigureAwait(false);

            return MapRows(table);
        }

        /// <inheritdoc/>
        public async Task<List<BackupJob>> ReadByPolicyAsync(string policyId, CancellationToken token = default)
        {
            if (String.IsNullOrWhiteSpace(policyId))
                throw new ArgumentNullException(nameof(policyId));

            DataTable table = await _Driver.ExecuteQueryAsync(
                "SELECT * FROM backup_jobs WHERE policy_id = " + Sanitizer.Literal(policyId) + " ORDER BY created_utc DESC, id DESC;", false, token).ConfigureAwait(false);

            return MapRows(table);
        }

        /// <inheritdoc/>
        public async Task<BackupJob?> ReadLatestCompletedAsync(string policyId, CancellationToken token = default)
        {
            if (String.IsNullOrWhiteSpace(policyId))
                throw new ArgumentNullException(nameof(policyId));

            DataTable table = await _Driver.ExecuteQueryAsync(
                "SELECT * FROM backup_jobs WHERE policy_id = " + Sanitizer.Literal(policyId) +
                " AND status = " + Sanitizer.Literal(JobStatusEnum.Completed.ToString()) +
                " ORDER BY completed_utc DESC, id DESC LIMIT 1;", false, token).ConfigureAwait(false);

            return table.Rows.Count == 0 ? null : MapRow(table.Rows[0]);
        }

        /// <inheritdoc/>
        public async Task<BackupJob?> ReadLatestCompletedFullAsync(string policyId, CancellationToken token = default)
        {
            if (String.IsNullOrWhiteSpace(policyId))
                throw new ArgumentNullException(nameof(policyId));

            DataTable table = await _Driver.ExecuteQueryAsync(
                "SELECT * FROM backup_jobs WHERE policy_id = " + Sanitizer.Literal(policyId) +
                " AND status = " + Sanitizer.Literal(JobStatusEnum.Completed.ToString()) +
                " AND backup_type = " + Sanitizer.Literal(BackupTypeEnum.Full.ToString()) +
                " ORDER BY completed_utc DESC, id DESC LIMIT 1;", false, token).ConfigureAwait(false);

            return table.Rows.Count == 0 ? null : MapRow(table.Rows[0]);
        }

        /// <inheritdoc/>
        public async Task<BackupJob> UpdateAsync(BackupJob job, CancellationToken token = default)
        {
            if (job == null)
                throw new ArgumentNullException(nameof(job));
            if (String.IsNullOrWhiteSpace(job.Id))
                throw new ArgumentException("Backup job id is required for update.", nameof(job));

            await _Driver.ExecuteQueryAsync(BuildUpsert(job, false), false, token).ConfigureAwait(false);
            return job;
        }

        /// <inheritdoc/>
        public async Task<bool> DeleteAsync(string id, CancellationToken token = default)
        {
            if (String.IsNullOrWhiteSpace(id))
                throw new ArgumentNullException(nameof(id));

            if (!await ExistsAsync(id, token).ConfigureAwait(false))
                return false;

            await _Driver.ExecuteQueryAsync(
                "DELETE FROM backup_jobs WHERE id = " + Sanitizer.Literal(id) + ";", false, token).ConfigureAwait(false);
            return true;
        }

        /// <inheritdoc/>
        public async Task SetScanCompleteAsync(string jobId, bool complete, CancellationToken token = default)
        {
            if (String.IsNullOrWhiteSpace(jobId))
                throw new ArgumentNullException(nameof(jobId));

            await _Driver.ExecuteQueryAsync(
                "UPDATE backup_jobs SET scan_complete = " + Sanitizer.Bool(complete) + " WHERE id = " + Sanitizer.Literal(jobId) + ";",
                false, token).ConfigureAwait(false);
        }

        /// <inheritdoc/>
        public async Task UpdateProgressAsync(string jobId, bool scanning, long filesDone, long filesTotal, long bytesDone, long bytesTotal, CancellationToken token = default)
        {
            if (String.IsNullOrWhiteSpace(jobId))
                throw new ArgumentNullException(nameof(jobId));

            await _Driver.ExecuteQueryAsync(
                "UPDATE backup_jobs SET progress_scanning = " + Sanitizer.Bool(scanning) +
                ", progress_files_done = " + Sanitizer.Int(filesDone) +
                ", progress_files_total = " + Sanitizer.Int(filesTotal) +
                ", progress_bytes_done = " + Sanitizer.Int(bytesDone) +
                ", progress_bytes_total = " + Sanitizer.Int(bytesTotal) +
                " WHERE id = " + Sanitizer.Literal(jobId) + ";",
                false, token).ConfigureAwait(false);
        }

        /// <inheritdoc/>
        public async Task<bool> ExistsAsync(string id, CancellationToken token = default)
        {
            if (String.IsNullOrWhiteSpace(id))
                throw new ArgumentNullException(nameof(id));

            DataTable table = await _Driver.ExecuteQueryAsync(
                "SELECT COUNT(*) AS count FROM backup_jobs WHERE id = " + Sanitizer.Literal(id) + ";", false, token).ConfigureAwait(false);

            return table.Rows.Count > 0 && Converters.GetLong(table.Rows[0], "count") > 0;
        }

        private static string BuildUpsert(BackupJob job, bool insert)
        {
            if (insert)
            {
                return "INSERT INTO backup_jobs (id, policy_id, backup_type, base_job_id, status, manifest_key, started_utc, completed_utc, file_count, bytes_total, bytes_written, bytes_deduplicated, chunks_written, chunks_reused, scan_complete, skipped_files, skipped_bytes, error, created_utc) VALUES (" +
                    Sanitizer.Literal(job.Id) + ", " +
                    Sanitizer.Literal(job.PolicyId) + ", " +
                    Sanitizer.Literal(job.BackupType.ToString()) + ", " +
                    Sanitizer.Quote(job.BaseJobId) + ", " +
                    Sanitizer.Literal(job.Status.ToString()) + ", " +
                    Sanitizer.Quote(job.ManifestKey) + ", " +
                    Sanitizer.TimestampNullable(job.StartedUtc) + ", " +
                    Sanitizer.TimestampNullable(job.CompletedUtc) + ", " +
                    Sanitizer.Int(job.FileCount) + ", " +
                    Sanitizer.Int(job.BytesTotal) + ", " +
                    Sanitizer.Int(job.BytesWritten) + ", " +
                    Sanitizer.Int(job.BytesDeduplicated) + ", " +
                    Sanitizer.Int(job.ChunksWritten) + ", " +
                    Sanitizer.Int(job.ChunksReused) + ", " +
                    Sanitizer.Bool(job.ScanComplete) + ", " +
                    Sanitizer.Int(job.SkippedFiles) + ", " +
                    Sanitizer.Int(job.SkippedBytes) + ", " +
                    Sanitizer.Quote(job.Error) + ", " +
                    Sanitizer.Timestamp(job.CreatedUtc) + ");";
            }

            return "UPDATE backup_jobs SET " +
                "policy_id = " + Sanitizer.Literal(job.PolicyId) + ", " +
                "backup_type = " + Sanitizer.Literal(job.BackupType.ToString()) + ", " +
                "base_job_id = " + Sanitizer.Quote(job.BaseJobId) + ", " +
                "status = " + Sanitizer.Literal(job.Status.ToString()) + ", " +
                "manifest_key = " + Sanitizer.Quote(job.ManifestKey) + ", " +
                "started_utc = " + Sanitizer.TimestampNullable(job.StartedUtc) + ", " +
                "completed_utc = " + Sanitizer.TimestampNullable(job.CompletedUtc) + ", " +
                "file_count = " + Sanitizer.Int(job.FileCount) + ", " +
                "bytes_total = " + Sanitizer.Int(job.BytesTotal) + ", " +
                "bytes_written = " + Sanitizer.Int(job.BytesWritten) + ", " +
                "bytes_deduplicated = " + Sanitizer.Int(job.BytesDeduplicated) + ", " +
                "chunks_written = " + Sanitizer.Int(job.ChunksWritten) + ", " +
                "chunks_reused = " + Sanitizer.Int(job.ChunksReused) + ", " +
                "scan_complete = " + Sanitizer.Bool(job.ScanComplete) + ", " +
                "skipped_files = " + Sanitizer.Int(job.SkippedFiles) + ", " +
                "skipped_bytes = " + Sanitizer.Int(job.SkippedBytes) + ", " +
                "error = " + Sanitizer.Quote(job.Error) + " " +
                "WHERE id = " + Sanitizer.Literal(job.Id) + ";";
        }

        private static List<BackupJob> MapRows(DataTable table)
        {
            List<BackupJob> list = new List<BackupJob>();
            foreach (DataRow row in table.Rows)
                list.Add(MapRow(row));
            return list;
        }

        private static BackupJob MapRow(DataRow row)
        {
            BackupJob job = new BackupJob();
            job.Id = Converters.GetString(row, "id");
            job.PolicyId = Converters.GetString(row, "policy_id");
            job.BackupType = Converters.GetEnum<BackupTypeEnum>(row, "backup_type", BackupTypeEnum.Full);
            job.BaseJobId = Converters.GetStringOrNull(row, "base_job_id");
            job.Status = Converters.GetEnum<JobStatusEnum>(row, "status", JobStatusEnum.Pending);
            job.ManifestKey = Converters.GetStringOrNull(row, "manifest_key");
            job.StartedUtc = Converters.GetDateTimeOrNull(row, "started_utc");
            job.CompletedUtc = Converters.GetDateTimeOrNull(row, "completed_utc");
            job.FileCount = Converters.GetLong(row, "file_count");
            job.BytesTotal = Converters.GetLong(row, "bytes_total");
            job.BytesWritten = Converters.GetLong(row, "bytes_written");
            job.BytesDeduplicated = Converters.GetLong(row, "bytes_deduplicated");
            job.ChunksWritten = Converters.GetLong(row, "chunks_written");
            job.ChunksReused = Converters.GetLong(row, "chunks_reused");
            job.ScanComplete = Converters.GetBool(row, "scan_complete");
            job.SkippedFiles = Converters.GetLong(row, "skipped_files");
            job.SkippedBytes = Converters.GetLong(row, "skipped_bytes");
            job.ProgressScanning = Converters.GetBool(row, "progress_scanning");
            job.ProgressFilesDone = Converters.GetLong(row, "progress_files_done");
            job.ProgressFilesTotal = Converters.GetLong(row, "progress_files_total");
            job.ProgressBytesDone = Converters.GetLong(row, "progress_bytes_done");
            job.ProgressBytesTotal = Converters.GetLong(row, "progress_bytes_total");
            job.Error = Converters.GetStringOrNull(row, "error");
            job.CreatedUtc = Converters.GetDateTime(row, "created_utc");
            return job;
        }
    }
}
