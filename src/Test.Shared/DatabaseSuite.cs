namespace Test.Shared
{
    using System;
    using System.Collections.Generic;
    using System.Threading;
    using System.Threading.Tasks;
    using Armor.Core.Database;
    using Armor.Core.Enums;
    using Armor.Core.Models;
    using Touchstone.Core;

    /// <summary>
    /// Verifies the SQLite data layer: migrations, and create/read/update/delete behavior for every
    /// entity, including negative cases (null arguments, missing rows, and duplicate handling).
    /// </summary>
    public static class DatabaseSuite
    {
        /// <summary>
        /// Build the data-layer test suite.
        /// </summary>
        /// <returns>The database suite descriptor.</returns>
        public static TestSuiteDescriptor Build()
        {
            return new TestSuiteDescriptor(
                suiteId: "Database",
                displayName: "Data Layer",
                cases: new List<TestCaseDescriptor>
                {
                    Case("MigrationsIdempotent", "Migrations apply once and re-initialize cleanly", async ct =>
                    {
                        using (TempWorkspace ws = new TempWorkspace())
                        {
                            string file = ws.Combine("armor.db");
                            using (DatabaseDriverBase first = await OpenAsync(file, ct).ConfigureAwait(false))
                            {
                            }
                            using (DatabaseDriverBase second = await OpenAsync(file, ct).ConfigureAwait(false))
                            {
                                Policy? none = await second.Policies.ReadAsync("pol_missing", ct).ConfigureAwait(false);
                                Check.Null(none, "no policy should exist in a fresh database");
                            }
                        }
                    }),

                    Case("PolicyCrudWithChildren", "Policy create/read/update/delete preserves children", async ct =>
                    {
                        using (TempWorkspace ws = new TempWorkspace())
                        using (DatabaseDriverBase db = await OpenAsync(ws.Combine("armor.db"), ct).ConfigureAwait(false))
                        {
                            Policy policy = new Policy();
                            policy.Name = "Documents";
                            policy.IncludePaths.Add("/data/a");
                            policy.IncludePaths.Add("/data/b");
                            policy.ExcludePatterns.Add(new ExcludePattern("*.tmp", false, ExcludeTargetEnum.File));
                            policy.ExcludePatterns.Add(new ExcludePattern("^cache$", true, ExcludeTargetEnum.Directory));
                            policy.BackupType = BackupTypeEnum.Incremental;
                            policy.RetentionDays = 45;
                            policy.MaxParallelism = 6;

                            await db.Policies.CreateAsync(policy, ct).ConfigureAwait(false);

                            Policy? read = await db.Policies.ReadAsync(policy.Id, ct).ConfigureAwait(false);
                            Check.NotNull(read, "policy should be readable");
                            Check.Equal("Documents", read!.Name, "name round-trips");
                            Check.Equal(2, read.IncludePaths.Count, "include paths round-trip");
                            Check.Equal("/data/a", read.IncludePaths[0], "include path order preserved");
                            Check.Equal(2, read.ExcludePatterns.Count, "exclude patterns round-trip");
                            Check.True(read.ExcludePatterns[1].IsRegex, "regex flag round-trips");
                            Check.Equal(ExcludeTargetEnum.Directory, read.ExcludePatterns[1].Target, "target round-trips");
                            Check.Equal(BackupTypeEnum.Incremental, read.BackupType, "backup type round-trips");
                            Check.Equal(45, read.RetentionDays, "retention round-trips");
                            Check.Equal(6, read.MaxParallelism, "max parallelism round-trips");

                            Policy clampCheck = new Policy();
                            clampCheck.MaxParallelism = 9999;
                            Check.Equal(Policy.MaxParallelismLimit, clampCheck.MaxParallelism, "parallelism clamps to the upper limit");
                            clampCheck.MaxParallelism = 0;
                            Check.Equal(Policy.MinParallelism, clampCheck.MaxParallelism, "parallelism clamps to the lower limit");

                            read.Name = "Docs";
                            read.IncludePaths.Clear();
                            read.IncludePaths.Add("/only");
                            read.ExcludePatterns.Clear();
                            await db.Policies.UpdateAsync(read, ct).ConfigureAwait(false);

                            Policy? updated = await db.Policies.ReadAsync(policy.Id, ct).ConfigureAwait(false);
                            Check.Equal("Docs", updated!.Name, "update persists name");
                            Check.Equal(1, updated.IncludePaths.Count, "update replaces include paths");
                            Check.Equal(0, updated.ExcludePatterns.Count, "update clears exclude patterns");

                            bool deleted = await db.Policies.DeleteAsync(policy.Id, ct).ConfigureAwait(false);
                            Check.True(deleted, "delete returns true");
                            Check.Null(await db.Policies.ReadAsync(policy.Id, ct).ConfigureAwait(false), "policy gone after delete");
                        }
                    }),

                    Case("PolicyMissingAndNullArgs", "Policy negative cases", async ct =>
                    {
                        using (TempWorkspace ws = new TempWorkspace())
                        using (DatabaseDriverBase db = await OpenAsync(ws.Combine("armor.db"), ct).ConfigureAwait(false))
                        {
                            Check.Null(await db.Policies.ReadAsync("pol_nope", ct).ConfigureAwait(false), "missing read is null");
                            Check.False(await db.Policies.DeleteAsync("pol_nope", ct).ConfigureAwait(false), "missing delete is false");
                            Check.False(await db.Policies.ExistsAsync("pol_nope", ct).ConfigureAwait(false), "missing exists is false");
                            await Check.ThrowsAsync<ArgumentNullException>(() => db.Policies.CreateAsync(null!, ct), "create null throws").ConfigureAwait(false);
                            await Check.ThrowsAsync<ArgumentNullException>(() => db.Policies.ReadAsync("", ct), "read empty id throws").ConfigureAwait(false);
                        }
                    }),

                    Case("ScheduleCrud", "Schedule create/read/update/delete and read-by-policy", async ct =>
                    {
                        using (TempWorkspace ws = new TempWorkspace())
                        using (DatabaseDriverBase db = await OpenAsync(ws.Combine("armor.db"), ct).ConfigureAwait(false))
                        {
                            Schedule schedule = new Schedule();
                            schedule.PolicyId = "pol_x";
                            schedule.CronExpression = "0 2 * * *";
                            await db.Schedules.CreateAsync(schedule, ct).ConfigureAwait(false);

                            Schedule? read = await db.Schedules.ReadAsync(schedule.Id, ct).ConfigureAwait(false);
                            Check.NotNull(read, "schedule readable");
                            Check.Equal("0 2 * * *", read!.CronExpression, "cron round-trips");

                            read.Enabled = false;
                            read.NextRunUtc = DateTime.UtcNow;
                            await db.Schedules.UpdateAsync(read, ct).ConfigureAwait(false);
                            Schedule? updated = await db.Schedules.ReadAsync(schedule.Id, ct).ConfigureAwait(false);
                            Check.False(updated!.Enabled, "enabled updated");
                            Check.NotNull(updated.NextRunUtc, "next run persisted");

                            List<Schedule> byPolicy = await db.Schedules.ReadByPolicyAsync("pol_x", ct).ConfigureAwait(false);
                            Check.Equal(1, byPolicy.Count, "read-by-policy finds the schedule");

                            Check.True(await db.Schedules.DeleteAsync(schedule.Id, ct).ConfigureAwait(false), "delete returns true");
                            await Check.ThrowsAsync<ArgumentNullException>(() => db.Schedules.CreateAsync(null!, ct), "create null throws").ConfigureAwait(false);
                        }
                    }),

                    Case("StorageTargetCrud", "Storage target round-trips its union of fields", async ct =>
                    {
                        using (TempWorkspace ws = new TempWorkspace())
                        using (DatabaseDriverBase db = await OpenAsync(ws.Combine("armor.db"), ct).ConfigureAwait(false))
                        {
                            StorageTarget target = new StorageTarget();
                            target.Name = "S3 Primary";
                            target.Type = StorageTargetTypeEnum.AmazonS3;
                            target.Region = "us-east-2";
                            target.Bucket = "backups";
                            target.AccessKey = "AKIA";
                            target.SecretKey = "secret'value";
                            target.UseSsl = true;
                            await db.StorageTargets.CreateAsync(target, ct).ConfigureAwait(false);

                            StorageTarget? read = await db.StorageTargets.ReadAsync(target.Id, ct).ConfigureAwait(false);
                            Check.NotNull(read, "target readable");
                            Check.Equal(StorageTargetTypeEnum.AmazonS3, read!.Type, "type round-trips");
                            Check.Equal("us-east-2", read.Region, "region round-trips");
                            Check.Equal("secret'value", read.SecretKey, "secret with quote round-trips");

                            read.Bucket = "backups-2";
                            await db.StorageTargets.UpdateAsync(read, ct).ConfigureAwait(false);
                            Check.Equal("backups-2", (await db.StorageTargets.ReadAsync(target.Id, ct).ConfigureAwait(false))!.Bucket, "bucket updated");

                            List<StorageTarget> all = await db.StorageTargets.ReadAllAsync(ct).ConfigureAwait(false);
                            Check.Equal(1, all.Count, "read all finds one");
                            Check.True(await db.StorageTargets.DeleteAsync(target.Id, ct).ConfigureAwait(false), "delete returns true");
                        }
                    }),

                    Case("EncryptionKeyCrud", "Encryption key entry round-trips", async ct =>
                    {
                        using (TempWorkspace ws = new TempWorkspace())
                        using (DatabaseDriverBase db = await OpenAsync(ws.Combine("armor.db"), ct).ConfigureAwait(false))
                        {
                            EncryptionKey key = new EncryptionKey();
                            key.Name = "Primary Key";
                            key.UsesPassphrase = true;
                            key.KdfSaltBase64 = "c2FsdA==";
                            key.PassphraseWrappedKeyBase64 = "d3JhcHBlZA==";
                            await db.EncryptionKeys.CreateAsync(key, ct).ConfigureAwait(false);

                            EncryptionKey? read = await db.EncryptionKeys.ReadAsync(key.Id, ct).ConfigureAwait(false);
                            Check.NotNull(read, "key readable");
                            Check.True(read!.UsesPassphrase, "uses-passphrase round-trips");
                            Check.Equal("c2FsdA==", read.KdfSaltBase64, "salt round-trips");
                            Check.Equal("AES-256-GCM", read.CipherName, "cipher default round-trips");

                            Check.True(await db.EncryptionKeys.DeleteAsync(key.Id, ct).ConfigureAwait(false), "delete returns true");
                        }
                    }),

                    Case("BackupJobLifecycle", "Backup job create/update and latest lookups", async ct =>
                    {
                        using (TempWorkspace ws = new TempWorkspace())
                        using (DatabaseDriverBase db = await OpenAsync(ws.Combine("armor.db"), ct).ConfigureAwait(false))
                        {
                            BackupJob full = new BackupJob();
                            full.PolicyId = "pol_a";
                            full.BackupType = BackupTypeEnum.Full;
                            full.Status = JobStatusEnum.Completed;
                            full.CompletedUtc = DateTime.UtcNow.AddMinutes(-10);
                            await db.BackupJobs.CreateAsync(full, ct).ConfigureAwait(false);

                            BackupJob incr = new BackupJob();
                            incr.PolicyId = "pol_a";
                            incr.BackupType = BackupTypeEnum.Incremental;
                            incr.Status = JobStatusEnum.Completed;
                            incr.BaseJobId = full.Id;
                            incr.CompletedUtc = DateTime.UtcNow;
                            await db.BackupJobs.CreateAsync(incr, ct).ConfigureAwait(false);

                            BackupJob? latest = await db.BackupJobs.ReadLatestCompletedAsync("pol_a", ct).ConfigureAwait(false);
                            Check.Equal(incr.Id, latest!.Id, "latest completed is the incremental");

                            BackupJob? latestFull = await db.BackupJobs.ReadLatestCompletedFullAsync("pol_a", ct).ConfigureAwait(false);
                            Check.Equal(full.Id, latestFull!.Id, "latest full is the full run");

                            List<BackupJob> byPolicy = await db.BackupJobs.ReadByPolicyAsync("pol_a", ct).ConfigureAwait(false);
                            Check.Equal(2, byPolicy.Count, "two jobs for the policy");

                            incr.Status = JobStatusEnum.Failed;
                            incr.Error = "disk full";
                            await db.BackupJobs.UpdateAsync(incr, ct).ConfigureAwait(false);
                            BackupJob? reread = await db.BackupJobs.ReadAsync(incr.Id, ct).ConfigureAwait(false);
                            Check.Equal(JobStatusEnum.Failed, reread!.Status, "status updated");
                            Check.Equal("disk full", reread.Error, "error persisted");
                        }
                    }),

                    Case("RestoreJobCrud", "Restore job round-trips", async ct =>
                    {
                        using (TempWorkspace ws = new TempWorkspace())
                        using (DatabaseDriverBase db = await OpenAsync(ws.Combine("armor.db"), ct).ConfigureAwait(false))
                        {
                            RestoreJob job = new RestoreJob();
                            job.BackupJobId = "job_1";
                            job.Scope = RestoreScopeEnum.Folder;
                            job.SourceSelector = "/data/reports";
                            job.DestinationRoot = "/restore";
                            await db.RestoreJobs.CreateAsync(job, ct).ConfigureAwait(false);

                            RestoreJob? read = await db.RestoreJobs.ReadAsync(job.Id, ct).ConfigureAwait(false);
                            Check.Equal(RestoreScopeEnum.Folder, read!.Scope, "scope round-trips");
                            Check.Equal("/data/reports", read.SourceSelector, "selector round-trips");

                            read.Status = JobStatusEnum.Completed;
                            read.FilesRestored = 12;
                            await db.RestoreJobs.UpdateAsync(read, ct).ConfigureAwait(false);
                            Check.Equal(12, (await db.RestoreJobs.ReadAsync(job.Id, ct).ConfigureAwait(false))!.FilesRestored, "files restored persisted");
                        }
                    }),

                    Case("ChunkIndexReferenceCounting", "Chunk index dedup and reference counting", async ct =>
                    {
                        using (TempWorkspace ws = new TempWorkspace())
                        using (DatabaseDriverBase db = await OpenAsync(ws.Combine("armor.db"), ct).ConfigureAwait(false))
                        {
                            ChunkIndexEntry entry = new ChunkIndexEntry();
                            entry.StorageTargetId = "tgt_1";
                            entry.Hash = "abcdef";
                            entry.StoredSizeBytes = 100;
                            entry.PlaintextSizeBytes = 250;

                            ChunkIndexEntry first = await db.ChunkIndex.AddOrReferenceAsync(entry, ct).ConfigureAwait(false);
                            Check.Equal(1L, first.ReferenceCount, "first add sets ref count 1");

                            ChunkIndexEntry second = await db.ChunkIndex.AddOrReferenceAsync(entry, ct).ConfigureAwait(false);
                            Check.Equal(2L, second.ReferenceCount, "second add increments ref count");

                            long afterDec = await db.ChunkIndex.DecrementReferenceAsync("tgt_1", "abcdef", ct).ConfigureAwait(false);
                            Check.Equal(1L, afterDec, "decrement lowers ref count");

                            Check.True(await db.ChunkIndex.ExistsAsync("tgt_1", "abcdef", ct).ConfigureAwait(false), "chunk exists");
                            Check.Null(await db.ChunkIndex.ReadByHashAsync("tgt_1", "missing", ct).ConfigureAwait(false), "missing chunk is null");

                            long toZero = await db.ChunkIndex.DecrementReferenceAsync("tgt_1", "abcdef", ct).ConfigureAwait(false);
                            Check.Equal(0L, toZero, "decrement to zero");
                            long belowZero = await db.ChunkIndex.DecrementReferenceAsync("tgt_1", "abcdef", ct).ConfigureAwait(false);
                            Check.Equal(0L, belowZero, "decrement never goes below zero");

                            List<ChunkIndexEntry> unreferenced = await db.ChunkIndex.ReadUnreferencedAsync("tgt_1", ct).ConfigureAwait(false);
                            Check.Equal(1, unreferenced.Count, "one unreferenced chunk");

                            Check.Equal(-1L, await db.ChunkIndex.IncrementReferenceAsync("tgt_1", "ghost", ct).ConfigureAwait(false), "increment missing returns -1");
                            Check.True(await db.ChunkIndex.DeleteAsync("tgt_1", "abcdef", ct).ConfigureAwait(false), "delete removes chunk");
                        }
                    }),

                    Case("PolicyStateTable", "Per-policy state table upsert/read/delete/drop", async ct =>
                    {
                        using (TempWorkspace ws = new TempWorkspace())
                        using (DatabaseDriverBase db = await OpenAsync(ws.Combine("armor.db"), ct).ConfigureAwait(false))
                        {
                            string policyId = "pol_state1";
                            PolicyStateEntry entry = new PolicyStateEntry();
                            entry.Path = "/data/file.txt";
                            entry.SizeBytes = 1024;
                            entry.ModifiedUtc = DateTime.UtcNow;
                            entry.ChunkListHash = "hash1";

                            await db.PolicyState.UpsertAsync(policyId, entry, ct).ConfigureAwait(false);
                            PolicyStateEntry? read = await db.PolicyState.ReadAsync(policyId, "/data/file.txt", ct).ConfigureAwait(false);
                            Check.NotNull(read, "state readable");
                            Check.Equal(1024L, read!.SizeBytes, "size round-trips");
                            Check.Equal("hash1", read.ChunkListHash, "chunk list hash round-trips");

                            entry.SizeBytes = 2048;
                            entry.ChunkListHash = "hash2";
                            await db.PolicyState.UpsertAsync(policyId, entry, ct).ConfigureAwait(false);
                            Check.Equal(2048L, (await db.PolicyState.ReadAsync(policyId, "/data/file.txt", ct).ConfigureAwait(false))!.SizeBytes, "upsert updates size");

                            Check.Equal(1, (await db.PolicyState.ReadAllAsync(policyId, ct).ConfigureAwait(false)).Count, "read all returns one");
                            Check.True(await db.PolicyState.DeleteAsync(policyId, "/data/file.txt", ct).ConfigureAwait(false), "delete returns true");
                            Check.False(await db.PolicyState.DeleteAsync(policyId, "/data/file.txt", ct).ConfigureAwait(false), "second delete returns false");

                            await db.PolicyState.DropTableAsync(policyId, ct).ConfigureAwait(false);
                        }
                    }),

                    Case("JobFilesBatchedDelete", "Deleting a large work list removes every row", async ct =>
                    {
                        using (TempWorkspace ws = new TempWorkspace())
                        using (DatabaseDriverBase db = await OpenAsync(ws.Combine("armor.db"), ct).ConfigureAwait(false))
                        {
                            // Seed more rows than one delete batch (5000) so DeleteByJobAsync must loop; the
                            // batched loop must still remove every row and leave nothing pending.
                            const int count = 5001;
                            List<JobFileEntry> entries = new List<JobFileEntry>(count);
                            for (int i = 0; i < count; i++)
                            {
                                JobFileEntry entry = new JobFileEntry();
                                entry.Path = "/data/file-" + i + ".bin";
                                entry.SizeBytes = i;
                                entry.ModifiedUtc = DateTime.UtcNow;
                                entries.Add(entry);
                            }

                            await db.JobFiles.AddPendingAsync("job_batch", entries, ct).ConfigureAwait(false);
                            Check.True(await db.JobFiles.HasPendingAsync("job_batch", ct).ConfigureAwait(false), "seeded rows are pending");

                            await db.JobFiles.DeleteByJobAsync("job_batch", ct).ConfigureAwait(false);

                            Check.False(await db.JobFiles.HasPendingAsync("job_batch", ct).ConfigureAwait(false), "no rows remain pending");
                            Check.Equal(0, (await db.JobFiles.ReadPendingPageAsync("job_batch", 10, ct).ConfigureAwait(false)).Count, "work list is empty");
                        }
                    }),

                    Case("FactoryRejectsNull", "Driver factory rejects null settings", async ct =>
                    {
                        await Check.ThrowsAsync<ArgumentNullException>(
                            () => Task.Run(() => DatabaseDriverFactory.Create(null!), ct),
                            "factory create null throws").ConfigureAwait(false);
                    })
                });
        }

        private static TestCaseDescriptor Case(string caseId, string displayName, Func<CancellationToken, Task> body)
        {
            return new TestCaseDescriptor(
                suiteId: "Database",
                caseId: caseId,
                displayName: displayName,
                executeAsync: body);
        }

        private static async Task<DatabaseDriverBase> OpenAsync(string file, CancellationToken token)
        {
            DatabaseSettings settings = new DatabaseSettings(file);
            return await DatabaseDriverFactory.CreateAndInitializeAsync(settings, token).ConfigureAwait(false);
        }
    }
}
