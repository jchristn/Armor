namespace Armor.Core.Database.Sqlite.Implementations
{
    using System;
    using System.Collections.Generic;
    using System.Data;
    using System.Threading;
    using System.Threading.Tasks;
    using Armor.Core.Database.Interfaces;
    using Armor.Core.Helpers;
    using Armor.Core.Models;

    /// <summary>
    /// SQLite implementation of <see cref="IScheduleMethods"/>.
    /// </summary>
    public sealed class SqliteScheduleMethods : IScheduleMethods
    {
        private readonly DatabaseDriverBase _Driver;

        /// <summary>
        /// Initializes a new instance of the <see cref="SqliteScheduleMethods"/> class.
        /// </summary>
        /// <param name="driver">The owning database driver. Cannot be null.</param>
        /// <exception cref="ArgumentNullException">Thrown when <paramref name="driver"/> is null.</exception>
        public SqliteScheduleMethods(DatabaseDriverBase driver)
        {
            _Driver = driver ?? throw new ArgumentNullException(nameof(driver));
        }

        /// <inheritdoc/>
        public async Task<Schedule> CreateAsync(Schedule schedule, CancellationToken token = default)
        {
            if (schedule == null)
                throw new ArgumentNullException(nameof(schedule));
            if (String.IsNullOrWhiteSpace(schedule.Id))
                schedule.Id = IdGenerator.GenerateScheduleId();

            await _Driver.ExecuteQueryAsync(
                "INSERT INTO schedules (id, policy_id, cron_expression, enabled, last_run_utc, next_run_utc, created_utc) VALUES (" +
                Sanitizer.Literal(schedule.Id) + ", " +
                Sanitizer.Literal(schedule.PolicyId) + ", " +
                Sanitizer.Literal(schedule.CronExpression) + ", " +
                Sanitizer.Bool(schedule.Enabled) + ", " +
                Sanitizer.TimestampNullable(schedule.LastRunUtc) + ", " +
                Sanitizer.TimestampNullable(schedule.NextRunUtc) + ", " +
                Sanitizer.Timestamp(schedule.CreatedUtc) + ");", false, token).ConfigureAwait(false);

            return schedule;
        }

        /// <inheritdoc/>
        public async Task<Schedule?> ReadAsync(string id, CancellationToken token = default)
        {
            if (String.IsNullOrWhiteSpace(id))
                throw new ArgumentNullException(nameof(id));

            DataTable table = await _Driver.ExecuteQueryAsync(
                "SELECT * FROM schedules WHERE id = " + Sanitizer.Literal(id) + ";", false, token).ConfigureAwait(false);

            return table.Rows.Count == 0 ? null : MapRow(table.Rows[0]);
        }

        /// <inheritdoc/>
        public async Task<List<Schedule>> ReadAllAsync(CancellationToken token = default)
        {
            DataTable table = await _Driver.ExecuteQueryAsync(
                "SELECT * FROM schedules ORDER BY created_utc ASC, id ASC;", false, token).ConfigureAwait(false);

            return MapRows(table);
        }

        /// <inheritdoc/>
        public async Task<List<Schedule>> ReadByPolicyAsync(string policyId, CancellationToken token = default)
        {
            if (String.IsNullOrWhiteSpace(policyId))
                throw new ArgumentNullException(nameof(policyId));

            DataTable table = await _Driver.ExecuteQueryAsync(
                "SELECT * FROM schedules WHERE policy_id = " + Sanitizer.Literal(policyId) + " ORDER BY created_utc ASC, id ASC;", false, token).ConfigureAwait(false);

            return MapRows(table);
        }

        /// <inheritdoc/>
        public async Task<Schedule> UpdateAsync(Schedule schedule, CancellationToken token = default)
        {
            if (schedule == null)
                throw new ArgumentNullException(nameof(schedule));
            if (String.IsNullOrWhiteSpace(schedule.Id))
                throw new ArgumentException("Schedule id is required for update.", nameof(schedule));

            await _Driver.ExecuteQueryAsync(
                "UPDATE schedules SET " +
                "policy_id = " + Sanitizer.Literal(schedule.PolicyId) + ", " +
                "cron_expression = " + Sanitizer.Literal(schedule.CronExpression) + ", " +
                "enabled = " + Sanitizer.Bool(schedule.Enabled) + ", " +
                "last_run_utc = " + Sanitizer.TimestampNullable(schedule.LastRunUtc) + ", " +
                "next_run_utc = " + Sanitizer.TimestampNullable(schedule.NextRunUtc) + " " +
                "WHERE id = " + Sanitizer.Literal(schedule.Id) + ";", false, token).ConfigureAwait(false);

            return schedule;
        }

        /// <inheritdoc/>
        public async Task<bool> DeleteAsync(string id, CancellationToken token = default)
        {
            if (String.IsNullOrWhiteSpace(id))
                throw new ArgumentNullException(nameof(id));

            if (!await ExistsAsync(id, token).ConfigureAwait(false))
                return false;

            await _Driver.ExecuteQueryAsync(
                "DELETE FROM schedules WHERE id = " + Sanitizer.Literal(id) + ";", false, token).ConfigureAwait(false);
            return true;
        }

        /// <inheritdoc/>
        public async Task<bool> ExistsAsync(string id, CancellationToken token = default)
        {
            if (String.IsNullOrWhiteSpace(id))
                throw new ArgumentNullException(nameof(id));

            DataTable table = await _Driver.ExecuteQueryAsync(
                "SELECT COUNT(*) AS count FROM schedules WHERE id = " + Sanitizer.Literal(id) + ";", false, token).ConfigureAwait(false);

            return table.Rows.Count > 0 && Converters.GetLong(table.Rows[0], "count") > 0;
        }

        private static List<Schedule> MapRows(DataTable table)
        {
            List<Schedule> list = new List<Schedule>();
            foreach (DataRow row in table.Rows)
                list.Add(MapRow(row));
            return list;
        }

        private static Schedule MapRow(DataRow row)
        {
            Schedule schedule = new Schedule();
            schedule.Id = Converters.GetString(row, "id");
            schedule.PolicyId = Converters.GetString(row, "policy_id");
            schedule.CronExpression = Converters.GetString(row, "cron_expression");
            schedule.Enabled = Converters.GetBool(row, "enabled");
            schedule.LastRunUtc = Converters.GetDateTimeOrNull(row, "last_run_utc");
            schedule.NextRunUtc = Converters.GetDateTimeOrNull(row, "next_run_utc");
            schedule.CreatedUtc = Converters.GetDateTime(row, "created_utc");
            return schedule;
        }
    }
}
