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
    /// SQLite implementation of <see cref="IRestoreJobMethods"/>.
    /// </summary>
    public sealed class SqliteRestoreJobMethods : IRestoreJobMethods
    {
        private readonly DatabaseDriverBase _Driver;

        /// <summary>
        /// Initializes a new instance of the <see cref="SqliteRestoreJobMethods"/> class.
        /// </summary>
        /// <param name="driver">The owning database driver. Cannot be null.</param>
        /// <exception cref="ArgumentNullException">Thrown when <paramref name="driver"/> is null.</exception>
        public SqliteRestoreJobMethods(DatabaseDriverBase driver)
        {
            _Driver = driver ?? throw new ArgumentNullException(nameof(driver));
        }

        /// <inheritdoc/>
        public async Task<RestoreJob> CreateAsync(RestoreJob job, CancellationToken token = default)
        {
            if (job == null)
                throw new ArgumentNullException(nameof(job));
            if (String.IsNullOrWhiteSpace(job.Id))
                job.Id = IdGenerator.GenerateRestoreJobId();

            await _Driver.ExecuteQueryAsync(
                "INSERT INTO restore_jobs (id, backup_job_id, scope, source_selector, destination_root, status, started_utc, completed_utc, files_restored, bytes_restored, error, created_utc) VALUES (" +
                Sanitizer.Literal(job.Id) + ", " +
                Sanitizer.Literal(job.BackupJobId) + ", " +
                Sanitizer.Literal(job.Scope.ToString()) + ", " +
                Sanitizer.Quote(job.SourceSelector) + ", " +
                Sanitizer.Quote(job.DestinationRoot) + ", " +
                Sanitizer.Literal(job.Status.ToString()) + ", " +
                Sanitizer.TimestampNullable(job.StartedUtc) + ", " +
                Sanitizer.TimestampNullable(job.CompletedUtc) + ", " +
                Sanitizer.Int(job.FilesRestored) + ", " +
                Sanitizer.Int(job.BytesRestored) + ", " +
                Sanitizer.Quote(job.Error) + ", " +
                Sanitizer.Timestamp(job.CreatedUtc) + ");", false, token).ConfigureAwait(false);

            return job;
        }

        /// <inheritdoc/>
        public async Task<RestoreJob?> ReadAsync(string id, CancellationToken token = default)
        {
            if (String.IsNullOrWhiteSpace(id))
                throw new ArgumentNullException(nameof(id));

            DataTable table = await _Driver.ExecuteQueryAsync(
                "SELECT * FROM restore_jobs WHERE id = " + Sanitizer.Literal(id) + ";", false, token).ConfigureAwait(false);

            return table.Rows.Count == 0 ? null : MapRow(table.Rows[0]);
        }

        /// <inheritdoc/>
        public async Task<List<RestoreJob>> ReadAllAsync(CancellationToken token = default)
        {
            DataTable table = await _Driver.ExecuteQueryAsync(
                "SELECT * FROM restore_jobs ORDER BY created_utc DESC, id DESC;", false, token).ConfigureAwait(false);

            List<RestoreJob> list = new List<RestoreJob>();
            foreach (DataRow row in table.Rows)
                list.Add(MapRow(row));
            return list;
        }

        /// <inheritdoc/>
        public async Task<RestoreJob> UpdateAsync(RestoreJob job, CancellationToken token = default)
        {
            if (job == null)
                throw new ArgumentNullException(nameof(job));
            if (String.IsNullOrWhiteSpace(job.Id))
                throw new ArgumentException("Restore job id is required for update.", nameof(job));

            await _Driver.ExecuteQueryAsync(
                "UPDATE restore_jobs SET " +
                "backup_job_id = " + Sanitizer.Literal(job.BackupJobId) + ", " +
                "scope = " + Sanitizer.Literal(job.Scope.ToString()) + ", " +
                "source_selector = " + Sanitizer.Quote(job.SourceSelector) + ", " +
                "destination_root = " + Sanitizer.Quote(job.DestinationRoot) + ", " +
                "status = " + Sanitizer.Literal(job.Status.ToString()) + ", " +
                "started_utc = " + Sanitizer.TimestampNullable(job.StartedUtc) + ", " +
                "completed_utc = " + Sanitizer.TimestampNullable(job.CompletedUtc) + ", " +
                "files_restored = " + Sanitizer.Int(job.FilesRestored) + ", " +
                "bytes_restored = " + Sanitizer.Int(job.BytesRestored) + ", " +
                "error = " + Sanitizer.Quote(job.Error) + " " +
                "WHERE id = " + Sanitizer.Literal(job.Id) + ";", false, token).ConfigureAwait(false);

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
                "DELETE FROM restore_jobs WHERE id = " + Sanitizer.Literal(id) + ";", false, token).ConfigureAwait(false);
            return true;
        }

        /// <inheritdoc/>
        public async Task<bool> ExistsAsync(string id, CancellationToken token = default)
        {
            if (String.IsNullOrWhiteSpace(id))
                throw new ArgumentNullException(nameof(id));

            DataTable table = await _Driver.ExecuteQueryAsync(
                "SELECT COUNT(*) AS count FROM restore_jobs WHERE id = " + Sanitizer.Literal(id) + ";", false, token).ConfigureAwait(false);

            return table.Rows.Count > 0 && Converters.GetLong(table.Rows[0], "count") > 0;
        }

        private static RestoreJob MapRow(DataRow row)
        {
            RestoreJob job = new RestoreJob();
            job.Id = Converters.GetString(row, "id");
            job.BackupJobId = Converters.GetString(row, "backup_job_id");
            job.Scope = Converters.GetEnum<RestoreScopeEnum>(row, "scope", RestoreScopeEnum.All);
            job.SourceSelector = Converters.GetStringOrNull(row, "source_selector");
            job.DestinationRoot = Converters.GetStringOrNull(row, "destination_root");
            job.Status = Converters.GetEnum<JobStatusEnum>(row, "status", JobStatusEnum.Pending);
            job.StartedUtc = Converters.GetDateTimeOrNull(row, "started_utc");
            job.CompletedUtc = Converters.GetDateTimeOrNull(row, "completed_utc");
            job.FilesRestored = Converters.GetLong(row, "files_restored");
            job.BytesRestored = Converters.GetLong(row, "bytes_restored");
            job.Error = Converters.GetStringOrNull(row, "error");
            job.CreatedUtc = Converters.GetDateTime(row, "created_utc");
            return job;
        }
    }
}
