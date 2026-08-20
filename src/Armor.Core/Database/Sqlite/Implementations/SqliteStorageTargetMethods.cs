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
    /// SQLite implementation of <see cref="IStorageTargetMethods"/>.
    /// </summary>
    public sealed class SqliteStorageTargetMethods : IStorageTargetMethods
    {
        private readonly DatabaseDriverBase _Driver;

        /// <summary>
        /// Initializes a new instance of the <see cref="SqliteStorageTargetMethods"/> class.
        /// </summary>
        /// <param name="driver">The owning database driver. Cannot be null.</param>
        /// <exception cref="ArgumentNullException">Thrown when <paramref name="driver"/> is null.</exception>
        public SqliteStorageTargetMethods(DatabaseDriverBase driver)
        {
            _Driver = driver ?? throw new ArgumentNullException(nameof(driver));
        }

        /// <inheritdoc/>
        public async Task<StorageTarget> CreateAsync(StorageTarget target, CancellationToken token = default)
        {
            if (target == null)
                throw new ArgumentNullException(nameof(target));
            if (String.IsNullOrWhiteSpace(target.Id))
                target.Id = IdGenerator.GenerateStorageTargetId();

            await _Driver.ExecuteQueryAsync(
                "INSERT INTO storage_targets (id, name, type, repository_root, disk_path, host, share_name, username, password, nfs_user_id, nfs_group_id, nfs_version, endpoint, use_ssl, base_url, region, bucket, access_key, secret_key, account_name, account_key, container, project_id, credential_json, created_utc) VALUES (" +
                Sanitizer.Literal(target.Id) + ", " +
                Sanitizer.Literal(target.Name) + ", " +
                Sanitizer.Literal(target.Type.ToString()) + ", " +
                Sanitizer.Quote(target.RepositoryRoot) + ", " +
                Sanitizer.Quote(target.DiskPath) + ", " +
                Sanitizer.Quote(target.Host) + ", " +
                Sanitizer.Quote(target.ShareName) + ", " +
                Sanitizer.Quote(target.Username) + ", " +
                Sanitizer.Quote(target.Password) + ", " +
                Sanitizer.Int(target.NfsUserId) + ", " +
                Sanitizer.Int(target.NfsGroupId) + ", " +
                Sanitizer.Quote(target.NfsVersion) + ", " +
                Sanitizer.Quote(target.Endpoint) + ", " +
                Sanitizer.Bool(target.UseSsl) + ", " +
                Sanitizer.Quote(target.BaseUrl) + ", " +
                Sanitizer.Quote(target.Region) + ", " +
                Sanitizer.Quote(target.Bucket) + ", " +
                Sanitizer.Quote(target.AccessKey) + ", " +
                Sanitizer.Quote(target.SecretKey) + ", " +
                Sanitizer.Quote(target.AccountName) + ", " +
                Sanitizer.Quote(target.AccountKey) + ", " +
                Sanitizer.Quote(target.Container) + ", " +
                Sanitizer.Quote(target.ProjectId) + ", " +
                Sanitizer.Quote(target.CredentialJson) + ", " +
                Sanitizer.Timestamp(target.CreatedUtc) + ");", false, token).ConfigureAwait(false);

            return target;
        }

        /// <inheritdoc/>
        public async Task<StorageTarget?> ReadAsync(string id, CancellationToken token = default)
        {
            if (String.IsNullOrWhiteSpace(id))
                throw new ArgumentNullException(nameof(id));

            DataTable table = await _Driver.ExecuteQueryAsync(
                "SELECT * FROM storage_targets WHERE id = " + Sanitizer.Literal(id) + ";", false, token).ConfigureAwait(false);

            return table.Rows.Count == 0 ? null : MapRow(table.Rows[0]);
        }

        /// <inheritdoc/>
        public async Task<List<StorageTarget>> ReadAllAsync(CancellationToken token = default)
        {
            DataTable table = await _Driver.ExecuteQueryAsync(
                "SELECT * FROM storage_targets ORDER BY created_utc ASC, id ASC;", false, token).ConfigureAwait(false);

            List<StorageTarget> list = new List<StorageTarget>();
            foreach (DataRow row in table.Rows)
                list.Add(MapRow(row));
            return list;
        }

        /// <inheritdoc/>
        public async Task<StorageTarget> UpdateAsync(StorageTarget target, CancellationToken token = default)
        {
            if (target == null)
                throw new ArgumentNullException(nameof(target));
            if (String.IsNullOrWhiteSpace(target.Id))
                throw new ArgumentException("Storage target id is required for update.", nameof(target));

            await _Driver.ExecuteQueryAsync(
                "UPDATE storage_targets SET " +
                "name = " + Sanitizer.Literal(target.Name) + ", " +
                "type = " + Sanitizer.Literal(target.Type.ToString()) + ", " +
                "repository_root = " + Sanitizer.Quote(target.RepositoryRoot) + ", " +
                "disk_path = " + Sanitizer.Quote(target.DiskPath) + ", " +
                "host = " + Sanitizer.Quote(target.Host) + ", " +
                "share_name = " + Sanitizer.Quote(target.ShareName) + ", " +
                "username = " + Sanitizer.Quote(target.Username) + ", " +
                "password = " + Sanitizer.Quote(target.Password) + ", " +
                "nfs_user_id = " + Sanitizer.Int(target.NfsUserId) + ", " +
                "nfs_group_id = " + Sanitizer.Int(target.NfsGroupId) + ", " +
                "nfs_version = " + Sanitizer.Quote(target.NfsVersion) + ", " +
                "endpoint = " + Sanitizer.Quote(target.Endpoint) + ", " +
                "use_ssl = " + Sanitizer.Bool(target.UseSsl) + ", " +
                "base_url = " + Sanitizer.Quote(target.BaseUrl) + ", " +
                "region = " + Sanitizer.Quote(target.Region) + ", " +
                "bucket = " + Sanitizer.Quote(target.Bucket) + ", " +
                "access_key = " + Sanitizer.Quote(target.AccessKey) + ", " +
                "secret_key = " + Sanitizer.Quote(target.SecretKey) + ", " +
                "account_name = " + Sanitizer.Quote(target.AccountName) + ", " +
                "account_key = " + Sanitizer.Quote(target.AccountKey) + ", " +
                "container = " + Sanitizer.Quote(target.Container) + ", " +
                "project_id = " + Sanitizer.Quote(target.ProjectId) + ", " +
                "credential_json = " + Sanitizer.Quote(target.CredentialJson) + " " +
                "WHERE id = " + Sanitizer.Literal(target.Id) + ";", false, token).ConfigureAwait(false);

            return target;
        }

        /// <inheritdoc/>
        public async Task<bool> DeleteAsync(string id, CancellationToken token = default)
        {
            if (String.IsNullOrWhiteSpace(id))
                throw new ArgumentNullException(nameof(id));

            if (!await ExistsAsync(id, token).ConfigureAwait(false))
                return false;

            await _Driver.ExecuteQueryAsync(
                "DELETE FROM storage_targets WHERE id = " + Sanitizer.Literal(id) + ";", false, token).ConfigureAwait(false);
            return true;
        }

        /// <inheritdoc/>
        public async Task<bool> ExistsAsync(string id, CancellationToken token = default)
        {
            if (String.IsNullOrWhiteSpace(id))
                throw new ArgumentNullException(nameof(id));

            DataTable table = await _Driver.ExecuteQueryAsync(
                "SELECT COUNT(*) AS count FROM storage_targets WHERE id = " + Sanitizer.Literal(id) + ";", false, token).ConfigureAwait(false);

            return table.Rows.Count > 0 && Converters.GetLong(table.Rows[0], "count") > 0;
        }

        private static StorageTarget MapRow(DataRow row)
        {
            StorageTarget target = new StorageTarget();
            target.Id = Converters.GetString(row, "id");
            target.Name = Converters.GetString(row, "name");
            target.Type = Converters.GetEnum<StorageTargetTypeEnum>(row, "type", StorageTargetTypeEnum.Disk);
            target.RepositoryRoot = Converters.GetStringOrNull(row, "repository_root");
            target.DiskPath = Converters.GetStringOrNull(row, "disk_path");
            target.Host = Converters.GetStringOrNull(row, "host");
            target.ShareName = Converters.GetStringOrNull(row, "share_name");
            target.Username = Converters.GetStringOrNull(row, "username");
            target.Password = Converters.GetStringOrNull(row, "password");
            target.NfsUserId = Converters.GetInt(row, "nfs_user_id");
            target.NfsGroupId = Converters.GetInt(row, "nfs_group_id");
            target.NfsVersion = Converters.GetStringOrNull(row, "nfs_version");
            target.Endpoint = Converters.GetStringOrNull(row, "endpoint");
            target.UseSsl = Converters.GetBool(row, "use_ssl");
            target.BaseUrl = Converters.GetStringOrNull(row, "base_url");
            target.Region = Converters.GetStringOrNull(row, "region");
            target.Bucket = Converters.GetStringOrNull(row, "bucket");
            target.AccessKey = Converters.GetStringOrNull(row, "access_key");
            target.SecretKey = Converters.GetStringOrNull(row, "secret_key");
            target.AccountName = Converters.GetStringOrNull(row, "account_name");
            target.AccountKey = Converters.GetStringOrNull(row, "account_key");
            target.Container = Converters.GetStringOrNull(row, "container");
            target.ProjectId = Converters.GetStringOrNull(row, "project_id");
            target.CredentialJson = Converters.GetStringOrNull(row, "credential_json");
            target.CreatedUtc = Converters.GetDateTime(row, "created_utc");
            return target;
        }
    }
}
