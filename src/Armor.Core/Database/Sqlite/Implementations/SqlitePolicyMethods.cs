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
    /// SQLite implementation of <see cref="IPolicyMethods"/>. Policies and their include-path and
    /// exclude-pattern child rows are written and removed together within a transaction.
    /// </summary>
    public sealed class SqlitePolicyMethods : IPolicyMethods
    {
        private readonly DatabaseDriverBase _Driver;

        /// <summary>
        /// Initializes a new instance of the <see cref="SqlitePolicyMethods"/> class.
        /// </summary>
        /// <param name="driver">The owning database driver. Cannot be null.</param>
        /// <exception cref="ArgumentNullException">Thrown when <paramref name="driver"/> is null.</exception>
        public SqlitePolicyMethods(DatabaseDriverBase driver)
        {
            _Driver = driver ?? throw new ArgumentNullException(nameof(driver));
        }

        /// <inheritdoc/>
        public async Task<Policy> CreateAsync(Policy policy, CancellationToken token = default)
        {
            if (policy == null)
                throw new ArgumentNullException(nameof(policy));
            if (String.IsNullOrWhiteSpace(policy.Id))
                policy.Id = IdGenerator.GeneratePolicyId();

            List<string> queries = new List<string>();
            queries.Add(BuildInsert(policy));
            queries.AddRange(BuildChildInserts(policy));

            await _Driver.ExecuteQueriesAsync(queries, true, token).ConfigureAwait(false);
            return policy;
        }

        /// <inheritdoc/>
        public async Task<Policy?> ReadAsync(string id, CancellationToken token = default)
        {
            if (String.IsNullOrWhiteSpace(id))
                throw new ArgumentNullException(nameof(id));

            DataTable table = await _Driver.ExecuteQueryAsync(
                "SELECT * FROM policies WHERE id = " + Sanitizer.Literal(id) + ";", false, token).ConfigureAwait(false);

            if (table.Rows.Count == 0)
                return null;

            Policy policy = MapRow(table.Rows[0]);
            await LoadChildrenAsync(policy, token).ConfigureAwait(false);
            return policy;
        }

        /// <inheritdoc/>
        public async Task<List<Policy>> ReadAllAsync(CancellationToken token = default)
        {
            DataTable table = await _Driver.ExecuteQueryAsync(
                "SELECT * FROM policies ORDER BY created_utc ASC, id ASC;", false, token).ConfigureAwait(false);

            List<Policy> policies = new List<Policy>();
            foreach (DataRow row in table.Rows)
            {
                Policy policy = MapRow(row);
                await LoadChildrenAsync(policy, token).ConfigureAwait(false);
                policies.Add(policy);
            }
            return policies;
        }

        /// <inheritdoc/>
        public async Task<Policy> UpdateAsync(Policy policy, CancellationToken token = default)
        {
            if (policy == null)
                throw new ArgumentNullException(nameof(policy));
            if (String.IsNullOrWhiteSpace(policy.Id))
                throw new ArgumentException("Policy id is required for update.", nameof(policy));

            List<string> queries = new List<string>();
            queries.Add(
                "UPDATE policies SET " +
                "name = " + Sanitizer.Literal(policy.Name) + ", " +
                "enabled = " + Sanitizer.Bool(policy.Enabled) + ", " +
                "min_file_size_bytes = " + Sanitizer.Int(policy.MinFileSizeBytes) + ", " +
                "max_file_size_bytes = " + Sanitizer.Int(policy.MaxFileSizeBytes) + ", " +
                "backup_type = " + Sanitizer.Literal(policy.BackupType.ToString()) + ", " +
                "use_archive_bit = " + Sanitizer.Bool(policy.UseArchiveBit) + ", " +
                "retention_days = " + Sanitizer.Int(policy.RetentionDays) + ", " +
                "max_parallelism = " + Sanitizer.Int(policy.MaxParallelism) + ", " +
                "storage_target_id = " + Sanitizer.Quote(policy.StorageTargetId) + ", " +
                "encryption_key_id = " + Sanitizer.Quote(policy.EncryptionKeyId) + " " +
                "WHERE id = " + Sanitizer.Literal(policy.Id) + ";");
            queries.Add("DELETE FROM policy_include_paths WHERE policy_id = " + Sanitizer.Literal(policy.Id) + ";");
            queries.Add("DELETE FROM policy_exclude_patterns WHERE policy_id = " + Sanitizer.Literal(policy.Id) + ";");
            queries.AddRange(BuildChildInserts(policy));

            await _Driver.ExecuteQueriesAsync(queries, true, token).ConfigureAwait(false);
            return policy;
        }

        /// <inheritdoc/>
        public async Task<bool> DeleteAsync(string id, CancellationToken token = default)
        {
            if (String.IsNullOrWhiteSpace(id))
                throw new ArgumentNullException(nameof(id));

            if (!await ExistsAsync(id, token).ConfigureAwait(false))
                return false;

            List<string> queries = new List<string>
            {
                "DELETE FROM policy_include_paths WHERE policy_id = " + Sanitizer.Literal(id) + ";",
                "DELETE FROM policy_exclude_patterns WHERE policy_id = " + Sanitizer.Literal(id) + ";",
                "DELETE FROM policies WHERE id = " + Sanitizer.Literal(id) + ";"
            };

            await _Driver.ExecuteQueriesAsync(queries, true, token).ConfigureAwait(false);
            return true;
        }

        /// <inheritdoc/>
        public async Task<bool> ExistsAsync(string id, CancellationToken token = default)
        {
            if (String.IsNullOrWhiteSpace(id))
                throw new ArgumentNullException(nameof(id));

            DataTable table = await _Driver.ExecuteQueryAsync(
                "SELECT COUNT(*) AS count FROM policies WHERE id = " + Sanitizer.Literal(id) + ";", false, token).ConfigureAwait(false);

            return table.Rows.Count > 0 && Converters.GetLong(table.Rows[0], "count") > 0;
        }

        private static string BuildInsert(Policy policy)
        {
            return "INSERT INTO policies (id, name, enabled, min_file_size_bytes, max_file_size_bytes, backup_type, use_archive_bit, retention_days, max_parallelism, storage_target_id, encryption_key_id, created_utc) VALUES (" +
                Sanitizer.Literal(policy.Id) + ", " +
                Sanitizer.Literal(policy.Name) + ", " +
                Sanitizer.Bool(policy.Enabled) + ", " +
                Sanitizer.Int(policy.MinFileSizeBytes) + ", " +
                Sanitizer.Int(policy.MaxFileSizeBytes) + ", " +
                Sanitizer.Literal(policy.BackupType.ToString()) + ", " +
                Sanitizer.Bool(policy.UseArchiveBit) + ", " +
                Sanitizer.Int(policy.RetentionDays) + ", " +
                Sanitizer.Int(policy.MaxParallelism) + ", " +
                Sanitizer.Quote(policy.StorageTargetId) + ", " +
                Sanitizer.Quote(policy.EncryptionKeyId) + ", " +
                Sanitizer.Timestamp(policy.CreatedUtc) + ");";
        }

        private static List<string> BuildChildInserts(Policy policy)
        {
            List<string> queries = new List<string>();

            for (int i = 0; i < policy.IncludePaths.Count; i++)
            {
                queries.Add("INSERT INTO policy_include_paths (policy_id, ordinal, path) VALUES (" +
                    Sanitizer.Literal(policy.Id) + ", " +
                    Sanitizer.Int(i) + ", " +
                    Sanitizer.Literal(policy.IncludePaths[i]) + ");");
            }

            for (int i = 0; i < policy.ExcludePatterns.Count; i++)
            {
                ExcludePattern pattern = policy.ExcludePatterns[i];
                queries.Add("INSERT INTO policy_exclude_patterns (policy_id, ordinal, pattern, is_regex, target) VALUES (" +
                    Sanitizer.Literal(policy.Id) + ", " +
                    Sanitizer.Int(i) + ", " +
                    Sanitizer.Literal(pattern.Pattern) + ", " +
                    Sanitizer.Bool(pattern.IsRegex) + ", " +
                    Sanitizer.Literal(pattern.Target.ToString()) + ");");
            }

            return queries;
        }

        private async Task LoadChildrenAsync(Policy policy, CancellationToken token)
        {
            DataTable includes = await _Driver.ExecuteQueryAsync(
                "SELECT path FROM policy_include_paths WHERE policy_id = " + Sanitizer.Literal(policy.Id) + " ORDER BY ordinal ASC;", false, token).ConfigureAwait(false);
            foreach (DataRow row in includes.Rows)
                policy.IncludePaths.Add(Converters.GetString(row, "path"));

            DataTable excludes = await _Driver.ExecuteQueryAsync(
                "SELECT pattern, is_regex, target FROM policy_exclude_patterns WHERE policy_id = " + Sanitizer.Literal(policy.Id) + " ORDER BY ordinal ASC;", false, token).ConfigureAwait(false);
            foreach (DataRow row in excludes.Rows)
            {
                ExcludePattern pattern = new ExcludePattern(
                    Converters.GetString(row, "pattern"),
                    Converters.GetBool(row, "is_regex"),
                    Converters.GetEnum<ExcludeTargetEnum>(row, "target", ExcludeTargetEnum.File));
                policy.ExcludePatterns.Add(pattern);
            }
        }

        private static Policy MapRow(DataRow row)
        {
            Policy policy = new Policy();
            policy.Id = Converters.GetString(row, "id");
            policy.Name = Converters.GetString(row, "name");
            policy.Enabled = Converters.GetBool(row, "enabled");
            policy.MinFileSizeBytes = Converters.GetLong(row, "min_file_size_bytes");
            policy.MaxFileSizeBytes = Converters.GetLong(row, "max_file_size_bytes");
            policy.BackupType = Converters.GetEnum<BackupTypeEnum>(row, "backup_type", BackupTypeEnum.Full);
            policy.UseArchiveBit = Converters.GetBool(row, "use_archive_bit");
            policy.RetentionDays = Converters.GetInt(row, "retention_days");
            policy.MaxParallelism = Converters.GetInt(row, "max_parallelism");
            policy.StorageTargetId = Converters.GetStringOrNull(row, "storage_target_id");
            policy.EncryptionKeyId = Converters.GetStringOrNull(row, "encryption_key_id");
            policy.CreatedUtc = Converters.GetDateTime(row, "created_utc");
            return policy;
        }
    }
}
