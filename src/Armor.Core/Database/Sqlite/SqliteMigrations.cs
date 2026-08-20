namespace Armor.Core.Database.Sqlite
{
    using System.Collections.Generic;

    /// <summary>
    /// Defines the ordered, idempotent SQLite schema migrations for Armor. Per-policy state tables
    /// are created on demand and are not part of this set.
    /// </summary>
    public static class SqliteMigrations
    {
        /// <summary>
        /// Return all schema migrations in ascending version order.
        /// </summary>
        /// <returns>The migration list.</returns>
        public static List<SchemaMigration> All()
        {
            List<SchemaMigration> migrations = new List<SchemaMigration>();

            migrations.Add(new SchemaMigration(
                1,
                "Initial schema",
                new List<string>
                {
                    "CREATE TABLE IF NOT EXISTS policies (" +
                    "id TEXT PRIMARY KEY, " +
                    "name TEXT NOT NULL, " +
                    "enabled INTEGER NOT NULL DEFAULT 1, " +
                    "min_file_size_bytes INTEGER NOT NULL DEFAULT 0, " +
                    "max_file_size_bytes INTEGER NOT NULL DEFAULT 0, " +
                    "backup_type TEXT NOT NULL, " +
                    "use_archive_bit INTEGER NOT NULL DEFAULT 0, " +
                    "retention_days INTEGER NOT NULL DEFAULT 30, " +
                    "storage_target_id TEXT NULL, " +
                    "encryption_key_id TEXT NULL, " +
                    "created_utc TEXT NOT NULL);",

                    "CREATE TABLE IF NOT EXISTS policy_include_paths (" +
                    "policy_id TEXT NOT NULL, " +
                    "ordinal INTEGER NOT NULL, " +
                    "path TEXT NOT NULL, " +
                    "PRIMARY KEY (policy_id, ordinal));",

                    "CREATE TABLE IF NOT EXISTS policy_exclude_patterns (" +
                    "policy_id TEXT NOT NULL, " +
                    "ordinal INTEGER NOT NULL, " +
                    "pattern TEXT NOT NULL, " +
                    "is_regex INTEGER NOT NULL DEFAULT 0, " +
                    "target TEXT NOT NULL, " +
                    "PRIMARY KEY (policy_id, ordinal));",

                    "CREATE TABLE IF NOT EXISTS schedules (" +
                    "id TEXT PRIMARY KEY, " +
                    "policy_id TEXT NOT NULL, " +
                    "cron_expression TEXT NOT NULL, " +
                    "enabled INTEGER NOT NULL DEFAULT 1, " +
                    "last_run_utc TEXT NULL, " +
                    "next_run_utc TEXT NULL, " +
                    "created_utc TEXT NOT NULL);",

                    "CREATE INDEX IF NOT EXISTS idx_schedules_policy ON schedules (policy_id);",

                    "CREATE TABLE IF NOT EXISTS storage_targets (" +
                    "id TEXT PRIMARY KEY, " +
                    "name TEXT NOT NULL, " +
                    "type TEXT NOT NULL, " +
                    "repository_root TEXT NULL, " +
                    "disk_path TEXT NULL, " +
                    "host TEXT NULL, " +
                    "share_name TEXT NULL, " +
                    "username TEXT NULL, " +
                    "password TEXT NULL, " +
                    "nfs_user_id INTEGER NOT NULL DEFAULT 0, " +
                    "nfs_group_id INTEGER NOT NULL DEFAULT 0, " +
                    "nfs_version TEXT NULL, " +
                    "endpoint TEXT NULL, " +
                    "use_ssl INTEGER NOT NULL DEFAULT 1, " +
                    "base_url TEXT NULL, " +
                    "region TEXT NULL, " +
                    "bucket TEXT NULL, " +
                    "access_key TEXT NULL, " +
                    "secret_key TEXT NULL, " +
                    "account_name TEXT NULL, " +
                    "account_key TEXT NULL, " +
                    "container TEXT NULL, " +
                    "project_id TEXT NULL, " +
                    "credential_json TEXT NULL, " +
                    "created_utc TEXT NOT NULL);",

                    "CREATE TABLE IF NOT EXISTS encryption_keys (" +
                    "id TEXT PRIMARY KEY, " +
                    "name TEXT NOT NULL, " +
                    "cipher_name TEXT NOT NULL, " +
                    "kdf_name TEXT NOT NULL, " +
                    "kdf_iterations INTEGER NOT NULL, " +
                    "kdf_salt_b64 TEXT NULL, " +
                    "uses_passphrase INTEGER NOT NULL DEFAULT 0, " +
                    "uses_keyfile INTEGER NOT NULL DEFAULT 0, " +
                    "passphrase_wrapped_b64 TEXT NULL, " +
                    "keyfile_wrapped_b64 TEXT NULL, " +
                    "created_utc TEXT NOT NULL);",

                    "CREATE TABLE IF NOT EXISTS backup_jobs (" +
                    "id TEXT PRIMARY KEY, " +
                    "policy_id TEXT NOT NULL, " +
                    "backup_type TEXT NOT NULL, " +
                    "base_job_id TEXT NULL, " +
                    "status TEXT NOT NULL, " +
                    "manifest_key TEXT NULL, " +
                    "started_utc TEXT NULL, " +
                    "completed_utc TEXT NULL, " +
                    "file_count INTEGER NOT NULL DEFAULT 0, " +
                    "bytes_total INTEGER NOT NULL DEFAULT 0, " +
                    "bytes_written INTEGER NOT NULL DEFAULT 0, " +
                    "bytes_deduplicated INTEGER NOT NULL DEFAULT 0, " +
                    "chunks_written INTEGER NOT NULL DEFAULT 0, " +
                    "chunks_reused INTEGER NOT NULL DEFAULT 0, " +
                    "error TEXT NULL, " +
                    "created_utc TEXT NOT NULL);",

                    "CREATE INDEX IF NOT EXISTS idx_backup_jobs_policy ON backup_jobs (policy_id);",

                    "CREATE TABLE IF NOT EXISTS restore_jobs (" +
                    "id TEXT PRIMARY KEY, " +
                    "backup_job_id TEXT NOT NULL, " +
                    "scope TEXT NOT NULL, " +
                    "source_selector TEXT NULL, " +
                    "destination_root TEXT NULL, " +
                    "status TEXT NOT NULL, " +
                    "started_utc TEXT NULL, " +
                    "completed_utc TEXT NULL, " +
                    "files_restored INTEGER NOT NULL DEFAULT 0, " +
                    "bytes_restored INTEGER NOT NULL DEFAULT 0, " +
                    "error TEXT NULL, " +
                    "created_utc TEXT NOT NULL);",

                    "CREATE INDEX IF NOT EXISTS idx_restore_jobs_backup ON restore_jobs (backup_job_id);",

                    "CREATE TABLE IF NOT EXISTS chunk_index (" +
                    "id TEXT NOT NULL, " +
                    "storage_target_id TEXT NOT NULL, " +
                    "hash TEXT NOT NULL, " +
                    "stored_size_bytes INTEGER NOT NULL DEFAULT 0, " +
                    "plaintext_size_bytes INTEGER NOT NULL DEFAULT 0, " +
                    "reference_count INTEGER NOT NULL DEFAULT 0, " +
                    "created_utc TEXT NOT NULL, " +
                    "PRIMARY KEY (storage_target_id, hash));",

                    "CREATE INDEX IF NOT EXISTS idx_chunk_index_refcount ON chunk_index (storage_target_id, reference_count);"
                }));

            return migrations;
        }
    }
}
