namespace Armor.Core.Database.Sqlite
{
    using System.Collections.Generic;
    using Armor.Core.Models;

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

            migrations.Add(new SchemaMigration(
                2,
                "Backup job work list",
                new List<string>
                {
                    // Durable per-file work list for a backup job: seeds during the scan, is marked done as
                    // files are processed, and is read back to assemble the manifest. It lets a run stream
                    // its manifest to disk and resume after a failure.
                    "CREATE TABLE IF NOT EXISTS job_files (" +
                    "job_id TEXT NOT NULL, " +
                    "path TEXT NOT NULL, " +
                    "size_bytes INTEGER NOT NULL DEFAULT 0, " +
                    "modified_utc TEXT NOT NULL, " +
                    "archive_bit INTEGER NOT NULL DEFAULT 0, " +
                    "done INTEGER NOT NULL DEFAULT 0, " +
                    "chunk_hashes TEXT NULL, " +
                    "PRIMARY KEY (job_id, path));",

                    "CREATE INDEX IF NOT EXISTS idx_job_files_pending ON job_files (job_id, done, path);"
                }));

            migrations.Add(new SchemaMigration(
                3,
                "Compact job work list",
                new List<string>
                {
                    // Rebuild job_files so a file's (often very long) path is stored only once — in the
                    // table — instead of also being duplicated into a (job_id, path) primary-key index and
                    // a (job_id, done, path) secondary index. Rows are addressed by rowid, and the only
                    // index is (job_id, done, id) which holds no paths. For a multi-million-file backup this
                    // cuts the on-disk work list roughly five-fold. Any in-flight work lists are discarded;
                    // an interrupted run simply re-scans on its next attempt.
                    "DROP TABLE IF EXISTS job_files;",

                    "CREATE TABLE job_files (" +
                    "id INTEGER PRIMARY KEY, " +
                    "job_id TEXT NOT NULL, " +
                    "path TEXT NOT NULL, " +
                    "size_bytes INTEGER NOT NULL DEFAULT 0, " +
                    "modified_utc TEXT NOT NULL, " +
                    "archive_bit INTEGER NOT NULL DEFAULT 0, " +
                    "done INTEGER NOT NULL DEFAULT 0, " +
                    "chunk_hashes TEXT NULL);",

                    "CREATE INDEX idx_job_files_job_done ON job_files (job_id, done, id);"
                }));

            migrations.Add(new SchemaMigration(
                4,
                "Heal bare-name excludes to match files and directories",
                new List<string>
                {
                    // Before this version a typed exclude with no trailing slash always defaulted to File,
                    // and directory pruning was only reachable by adding a slash. A bare name such as
                    // ".git" therefore matched a file named .git but never pruned the .git *directory*, so
                    // the walk descended into it and backed up everything inside. Every non-regex File rule
                    // was really "anything of this name", so promote them to Any (match both a file and a
                    // directory). Regex rules are left untouched: the graphical per-path excludes are stored
                    // as regex with a deliberate File or Directory target and must stay exact.
                    "UPDATE policy_exclude_patterns SET target = 'Any' WHERE is_regex = 0 AND target = 'File';"
                }));

            migrations.Add(new SchemaMigration(
                5,
                "Per-policy parallelism",
                new List<string>
                {
                    // How many files a run of the policy processes at once. Existing policies adopt the
                    // default; the value is clamped in the model when read and written.
                    "ALTER TABLE policies ADD COLUMN max_parallelism INTEGER NOT NULL DEFAULT 4;"
                }));

            migrations.Add(new SchemaMigration(
                6,
                "Shared global exclude list",
                BuildGlobalExcludeMigration()));

            migrations.Add(new SchemaMigration(
                7,
                "Backup-job scan-complete flag",
                new List<string>
                {
                    // Marks whether a run finished scanning its source. A run now processes files while it is
                    // still scanning, so this flag lets a resume tell a complete work list (process only) from
                    // a partial one left by a mid-scan crash (discard and re-scan). Existing rows are for
                    // finished or abandoned runs and adopt the default 0; only live runs set it to 1.
                    "ALTER TABLE backup_jobs ADD COLUMN scan_complete INTEGER NOT NULL DEFAULT 0;"
                }));

            migrations.Add(new SchemaMigration(
                8,
                "Never-reused work-list rowids",
                new List<string>
                {
                    // Rebuild job_files with an AUTOINCREMENT id. Because a run now scans and processes at the
                    // same time, the work-list producer pages by id > cursor while the scanner appends new rows.
                    // A plain INTEGER PRIMARY KEY lets SQLite reuse the id of a deleted max row (rows are deleted
                    // when a file vanishes between scan and copy); a reused id could land at or below the
                    // producer's cursor and be skipped, dropping a file from the backup. AUTOINCREMENT never
                    // reuses an id, so every newly scanned row sorts after the cursor. Any in-flight work list is
                    // discarded; an interrupted run simply re-scans on its next attempt.
                    "DROP TABLE IF EXISTS job_files;",

                    "CREATE TABLE job_files (" +
                    "id INTEGER PRIMARY KEY AUTOINCREMENT, " +
                    "job_id TEXT NOT NULL, " +
                    "path TEXT NOT NULL, " +
                    "size_bytes INTEGER NOT NULL DEFAULT 0, " +
                    "modified_utc TEXT NOT NULL, " +
                    "archive_bit INTEGER NOT NULL DEFAULT 0, " +
                    "done INTEGER NOT NULL DEFAULT 0, " +
                    "chunk_hashes TEXT NULL);",

                    "CREATE INDEX idx_job_files_job_done ON job_files (job_id, done, id);"
                }));

            migrations.Add(new SchemaMigration(
                9,
                "Backup-job skipped-file counters",
                new List<string>
                {
                    // Files skipped because they could not be read (locked, permission denied, a broken
                    // reparse point), and their total size, so a run can report how much it left behind.
                    "ALTER TABLE backup_jobs ADD COLUMN skipped_files INTEGER NOT NULL DEFAULT 0;",
                    "ALTER TABLE backup_jobs ADD COLUMN skipped_bytes INTEGER NOT NULL DEFAULT 0;"
                }));

            migrations.Add(new SchemaMigration(
                10,
                "Backup-job live-progress columns",
                new List<string>
                {
                    // Live progress a run flushes every few seconds so another process (the TUI's in-progress
                    // view) can draw the same progress bar it draws for a local run. These are display-only and
                    // never feed the final statistics, which come from the authoritative columns written when
                    // the run completes.
                    "ALTER TABLE backup_jobs ADD COLUMN progress_scanning INTEGER NOT NULL DEFAULT 0;",
                    "ALTER TABLE backup_jobs ADD COLUMN progress_files_done INTEGER NOT NULL DEFAULT 0;",
                    "ALTER TABLE backup_jobs ADD COLUMN progress_files_total INTEGER NOT NULL DEFAULT 0;",
                    "ALTER TABLE backup_jobs ADD COLUMN progress_bytes_done INTEGER NOT NULL DEFAULT 0;",
                    "ALTER TABLE backup_jobs ADD COLUMN progress_bytes_total INTEGER NOT NULL DEFAULT 0;"
                }));

            return migrations;
        }

        /// <summary>
        /// Build migration 6: create the shared global exclude table, seed it from the built-in defaults,
        /// and add the per-policy opt-in flag. The flag defaults to 1 so every existing policy immediately
        /// inherits the global list — the common build/cache/AppData noise leaves every backup without any
        /// per-policy edits. The seed rows are generated from <see cref="GlobalExcludeDefaults"/> so the
        /// seeded list and the TUI's "restore defaults" action share one definition.
        /// </summary>
        private static List<string> BuildGlobalExcludeMigration()
        {
            List<string> statements = new List<string>
            {
                "CREATE TABLE IF NOT EXISTS global_exclude_patterns (" +
                "ordinal INTEGER PRIMARY KEY, " +
                "pattern TEXT NOT NULL, " +
                "is_regex INTEGER NOT NULL DEFAULT 0, " +
                "target TEXT NOT NULL);",

                "ALTER TABLE policies ADD COLUMN use_global_excludes INTEGER NOT NULL DEFAULT 1;",
            };

            int ordinal = 0;
            foreach (ExcludePattern pattern in GlobalExcludeDefaults.Create())
            {
                statements.Add("INSERT INTO global_exclude_patterns (ordinal, pattern, is_regex, target) VALUES (" +
                    Sanitizer.Int(ordinal) + ", " +
                    Sanitizer.Literal(pattern.Pattern) + ", " +
                    Sanitizer.Bool(pattern.IsRegex) + ", " +
                    Sanitizer.Literal(pattern.Target.ToString()) + ");");
                ordinal++;
            }

            return statements;
        }
    }
}
