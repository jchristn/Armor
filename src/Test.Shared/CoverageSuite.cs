namespace Test.Shared
{
    using System;
    using System.Collections.Generic;
    using System.IO;
    using System.Threading;
    using System.Threading.Tasks;
    using Armor.Core.ChunkStore;
    using Armor.Core.Configuration;
    using Armor.Core.Database;
    using Armor.Core.Engine;
    using Armor.Core.Enums;
    using Armor.Core.Exceptions;
    using Armor.Core.Models;
    using Armor.Core.Scheduling;
    using Armor.Core.Security;
    using Armor.Core.Service;
    using Touchstone.Core;

    /// <summary>
    /// Exercises otherwise-untested deterministic code paths — enumerations, updates, disposal,
    /// change detection, path resolution, exceptions, and codecs — to keep the engine's coverage high.
    /// </summary>
    public static class CoverageSuite
    {
        /// <summary>
        /// Build the coverage test suite.
        /// </summary>
        /// <returns>The coverage suite descriptor.</returns>
        public static TestSuiteDescriptor Build()
        {
            return new TestSuiteDescriptor(
                suiteId: "Coverage",
                displayName: "Coverage Completeness",
                cases: new List<TestCaseDescriptor>
                {
                    Case("DataLayerEnumerationsAndUpdates", "Enumerations, updates, and missing-row paths", async ct =>
                    {
                        using (TempWorkspace ws = new TempWorkspace())
                        using (DatabaseDriverBase db = await OpenAsync(ws.Combine("armor.db"), ct).ConfigureAwait(false))
                        {
                            Schedule schedule = new Schedule();
                            schedule.PolicyId = "pol_c";
                            schedule.CronExpression = "0 0 * * *";
                            await db.Schedules.CreateAsync(schedule, ct).ConfigureAwait(false);
                            Check.Equal(1, (await db.Schedules.ReadAllAsync(ct).ConfigureAwait(false)).Count, "schedules read all");
                            Check.Null(await db.Schedules.ReadAsync("sch_missing", ct).ConfigureAwait(false), "missing schedule null");
                            Check.False(await db.Schedules.DeleteAsync("sch_missing", ct).ConfigureAwait(false), "delete missing schedule false");

                            EncryptionKey key = new EncryptionKey();
                            key.Name = "k";
                            await db.EncryptionKeys.CreateAsync(key, ct).ConfigureAwait(false);
                            key.Name = "k2";
                            await db.EncryptionKeys.UpdateAsync(key, ct).ConfigureAwait(false);
                            Check.Equal("k2", (await db.EncryptionKeys.ReadAsync(key.Id, ct).ConfigureAwait(false))!.Name, "key updated");
                            Check.Equal(1, (await db.EncryptionKeys.ReadAllAsync(ct).ConfigureAwait(false)).Count, "keys read all");

                            RestoreJob restore = new RestoreJob();
                            restore.BackupJobId = "job_x";
                            await db.RestoreJobs.CreateAsync(restore, ct).ConfigureAwait(false);
                            Check.True(await db.RestoreJobs.ExistsAsync(restore.Id, ct).ConfigureAwait(false), "restore exists");
                            Check.Equal(1, (await db.RestoreJobs.ReadAllAsync(ct).ConfigureAwait(false)).Count, "restores read all");
                            Check.True(await db.RestoreJobs.DeleteAsync(restore.Id, ct).ConfigureAwait(false), "restore deleted");

                            BackupJob job = new BackupJob();
                            job.PolicyId = "pol_c";
                            await db.BackupJobs.CreateAsync(job, ct).ConfigureAwait(false);
                            Check.Equal(1, (await db.BackupJobs.ReadAllAsync(ct).ConfigureAwait(false)).Count, "backup jobs read all");
                            Check.True(await db.BackupJobs.DeleteAsync(job.Id, ct).ConfigureAwait(false), "backup job deleted");

                            Policy policy = new Policy();
                            policy.Name = "p";
                            policy.IncludePaths.Add("/a");
                            await db.Policies.CreateAsync(policy, ct).ConfigureAwait(false);
                            Check.Equal(1, (await db.Policies.ReadAllAsync(ct).ConfigureAwait(false)).Count, "policies read all");

                            await db.CloseAsync(ct).ConfigureAwait(false);
                        }
                    }),

                    Case("DriverDisposeAsync", "Driver disposes asynchronously", async ct =>
                    {
                        using (TempWorkspace ws = new TempWorkspace())
                        {
                            DatabaseDriverBase db = await OpenAsync(ws.Combine("armor.db"), ct).ConfigureAwait(false);
                            await db.DisposeAsync().ConfigureAwait(false);
                        }
                    }),

                    Case("ContextDisposeAsync", "Context disposes asynchronously", async ct =>
                    {
                        using (TempWorkspace ws = new TempWorkspace())
                        {
                            ArmorContext context = await ArmorContext.CreateAsync(new ArmorPaths(ws.Combine("home")), ct).ConfigureAwait(false);
                            await context.DisposeAsync().ConfigureAwait(false);
                        }
                    }),

                    Case("ServiceUpdatesAndUnlocks", "Storage-target update/validate and key-file unlock", async ct =>
                    {
                        using (TempWorkspace ws = new TempWorkspace())
                        using (ArmorContext context = await ArmorContext.CreateAsync(new ArmorPaths(ws.Combine("home")), ct).ConfigureAwait(false))
                        {
                            StorageTargetService targetService = new StorageTargetService(context.Database, context.CredentialProtector);
                            StorageTarget target = new StorageTarget();
                            target.Name = "disk";
                            target.Type = StorageTargetTypeEnum.Disk;
                            target.DiskPath = ws.Combine("repo");
                            await targetService.CreateAsync(target, ct).ConfigureAwait(false);
                            target.Name = "disk-renamed";
                            await targetService.UpdateAsync(target, ct).ConfigureAwait(false);
                            Check.True(await targetService.ValidateAsync(target.Id, ct).ConfigureAwait(false), "disk target validates");

                            EncryptionKeyService keyService = new EncryptionKeyService(context.Database);
                            byte[] keyFile = KeyMaterial.GenerateKeyFileBytes();
                            ProvisionedKey provisioned = await keyService.ProvisionAsync("kf", null, keyFile, 50000, ct).ConfigureAwait(false);
                            byte[] unlocked = await keyService.UnlockWithKeyFileAsync(provisioned.Key.Id, keyFile, ct).ConfigureAwait(false);
                            Check.Equal(provisioned.DataKey.Length, unlocked.Length, "key-file unlock returns a data key");

                            await Check.ThrowsAsync<ArmorException>(
                                () => keyService.UnlockWithPassphraseAsync("key_missing", "x", ct),
                                "missing key throws").ConfigureAwait(false);
                        }
                    }),

                    Sync("ChangeDetectorBranches", "Change detector covers its branches", () =>
                    {
                        using (TempWorkspace ws = new TempWorkspace())
                        {
                            string file = Path.Combine(ws.RootDirectory, "f.txt");
                            File.WriteAllText(file, "hello world");
                            FileInfo info = new FileInfo(file);

                            ChangeDetector detector = new ChangeDetector();
                            Check.True(detector.HasChanged(info, null, false), "no baseline is changed");

                            ManifestFileEntry baseline = new ManifestFileEntry();
                            baseline.Path = file;
                            baseline.SizeBytes = info.Length;
                            baseline.ModifiedUtc = info.LastWriteTimeUtc;
                            Check.False(detector.HasChanged(info, baseline, false), "matching baseline is unchanged");

                            ManifestFileEntry sizeChanged = new ManifestFileEntry();
                            sizeChanged.Path = file;
                            sizeChanged.SizeBytes = info.Length + 10;
                            sizeChanged.ModifiedUtc = info.LastWriteTimeUtc;
                            Check.True(detector.HasChanged(info, sizeChanged, false), "size change detected");

                            ManifestFileEntry timeChanged = new ManifestFileEntry();
                            timeChanged.Path = file;
                            timeChanged.SizeBytes = info.Length;
                            timeChanged.ModifiedUtc = info.LastWriteTimeUtc.AddHours(-1);
                            Check.True(detector.HasChanged(info, timeChanged, false), "timestamp change detected");

                            detector.ClearArchiveBit(file);
                        }
                    }),

                    Sync("ArmorPathsResolution", "Path resolution covers env and defaults", () =>
                    {
                        ArmorPaths explicitPaths = new ArmorPaths("/tmp/armor-x");
                        Check.True(explicitPaths.ConfigFilePath.Length > 0, "config path derived");
                        Check.True(explicitPaths.LogDirectory.Length > 0, "log dir derived");
                        Check.True(explicitPaths.StateDirectory.Length > 0, "state dir derived");
                        Check.True(explicitPaths.DefaultDatabasePath.Length > 0, "db path derived");

                        ArmorPaths defaultPaths = new ArmorPaths();
                        Check.True(defaultPaths.RootDirectory.Length > 0, "default root derived");
                    }),

                    Sync("ExceptionConstructors", "Domain exceptions expose their constructors", () =>
                    {
                        Exception inner = new InvalidOperationException("inner");
                        Check.NotNull(new ArmorException(), "armor default");
                        Check.Equal("m", new ArmorException("m").Message, "armor message");
                        Check.NotNull(new ArmorException("m", inner), "armor inner");
                        Check.NotNull(new ArmorConfigurationException(), "config default");
                        Check.NotNull(new ArmorConfigurationException("m", inner), "config inner");
                        Check.NotNull(new ArmorCryptoException(), "crypto default");
                        Check.NotNull(new ArmorCryptoException("m", inner), "crypto inner");
                        Check.NotNull(new ArmorStorageException(), "storage default");
                        Check.Equal("m", new ArmorStorageException("m").Message, "storage message");
                        Check.NotNull(new ArmorStorageException("m", inner), "storage inner");
                    }),

                    Sync("RepositoryHeaderRoundTrip", "Repository header maps to and from a key", () =>
                    {
                        EncryptionKey key = new EncryptionKey();
                        key.Name = "hdr";
                        key.UsesPassphrase = true;
                        key.KdfSaltBase64 = "c2FsdA==";
                        key.PassphraseWrappedKeyBase64 = "d3JhcA==";
                        ChunkingSettings chunking = new ChunkingSettings();

                        RepositoryHeader header = RepositoryHeader.FromEncryptionKey(key, chunking);
                        Check.Equal(key.Id, header.EncryptionKeyId, "header carries key id");
                        Check.Equal(chunking.AvgSizeBytes, header.ChunkAvgSizeBytes, "header carries chunk avg");

                        EncryptionKey recovered = header.ToEncryptionKey();
                        Check.Equal(key.Id, recovered.Id, "recovered id matches");
                        Check.True(recovered.UsesPassphrase, "recovered passphrase flag");
                    }),

                    Sync("ManifestCodecDirect", "Manifest codec round-trips and rejects tampering", () =>
                    {
                        byte[] dataKey = new byte[32];
                        for (int i = 0; i < dataKey.Length; i++) dataKey[i] = (byte)(i + 5);

                        Manifest manifest = new Manifest();
                        manifest.JobId = "job_codec";
                        manifest.PolicyId = "pol_codec";
                        ManifestFileEntry entry = new ManifestFileEntry();
                        entry.Path = "/f";
                        entry.ChunkHashes.Add("aa");
                        manifest.Files.Add(entry);

                        byte[] encoded = ManifestCodec.Encode(manifest, dataKey);
                        Manifest decoded = ManifestCodec.Decode(encoded, dataKey, "job_codec");
                        Check.Equal(1, decoded.Files.Count, "manifest round-trips");

                        encoded[encoded.Length - 1] ^= 0xFF;
                        try
                        {
                            ManifestCodec.Decode(encoded, dataKey, "job_codec");
                            throw new InvalidOperationException("Expected ArmorCryptoException.");
                        }
                        catch (ArmorCryptoException)
                        {
                        }
                    }),

                    Sync("RunLockRejectsUnsafeId", "Run lock rejects an unsafe policy id", () =>
                    {
                        using (TempWorkspace ws = new TempWorkspace())
                        {
                            RunLock runLock = new RunLock(ws.Combine("state"));
                            try
                            {
                                runLock.TryAcquire("../evil");
                                throw new InvalidOperationException("Expected ArgumentException.");
                            }
                            catch (ArgumentException)
                            {
                            }
                        }
                    }),

                    Sync("ScheduleEvaluatorCronMatch", "Evaluator falls back to cron matching", () =>
                    {
                        ScheduleEvaluator evaluator = new ScheduleEvaluator();
                        Schedule schedule = new Schedule();
                        schedule.PolicyId = "pol_s";
                        schedule.CronExpression = "* * * * *";
                        schedule.NextRunUtc = null;
                        Check.True(evaluator.IsDue(schedule, new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc)), "every-minute cron is due");
                        DateTime? next = evaluator.ComputeNextRun(schedule, new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc));
                        Check.NotNull(next, "next run computed");
                    }),

                    Case("PassphraseUnlockService", "Service unlocks a key by passphrase", async ct =>
                    {
                        using (TempWorkspace ws = new TempWorkspace())
                        using (ArmorContext context = await ArmorContext.CreateAsync(new ArmorPaths(ws.Combine("home")), ct).ConfigureAwait(false))
                        {
                            EncryptionKeyService keyService = new EncryptionKeyService(context.Database);
                            ProvisionedKey provisioned = await keyService.ProvisionAsync("pw", "the pass", null, 50000, ct).ConfigureAwait(false);
                            byte[] unlocked = await keyService.UnlockWithPassphraseAsync(provisioned.Key.Id, "the pass", ct).ConfigureAwait(false);
                            Check.Equal(provisioned.DataKey.Length, unlocked.Length, "passphrase unlock returns a data key");
                        }
                    }),

                    Case("ExecuteQueriesNoTransaction", "Multiple queries run without a transaction", async ct =>
                    {
                        using (TempWorkspace ws = new TempWorkspace())
                        using (DatabaseDriverBase db = await OpenAsync(ws.Combine("armor.db"), ct).ConfigureAwait(false))
                        {
                            List<string> queries = new List<string>
                            {
                                "CREATE TABLE IF NOT EXISTS cov_t (id INTEGER);",
                                "INSERT INTO cov_t (id) VALUES (1);",
                                "SELECT COUNT(*) AS c FROM cov_t;"
                            };
                            System.Data.DataTable table = await db.ExecuteQueriesAsync(queries, false, ct).ConfigureAwait(false);
                            Check.Equal(1L, Convert.ToInt64(table.Rows[0]["c"]), "non-transactional batch executed");
                        }
                    }),

                    Case("SchedulerFirstTickSchedulesForward", "First tick sets next-run without running", async ct =>
                    {
                        using (TempWorkspace ws = new TempWorkspace())
                        using (ArmorContext context = await ArmorContext.CreateAsync(new ArmorPaths(ws.Combine("home")), ct).ConfigureAwait(false))
                        {
                            Schedule schedule = new Schedule();
                            schedule.PolicyId = "pol_none";
                            schedule.CronExpression = "0 3 * * *";
                            schedule.NextRunUtc = null;
                            await context.Database.Schedules.CreateAsync(schedule, ct).ConfigureAwait(false);

                            SchedulerService scheduler = new SchedulerService(context);
                            int ran = await scheduler.TickAsync(_ => Task.FromResult<byte[]?>(null), DateTime.UtcNow, ct).ConfigureAwait(false);
                            Check.Equal(0, ran, "first tick runs nothing");
                            Schedule? updated = await context.Database.Schedules.ReadAsync(schedule.Id, ct).ConfigureAwait(false);
                            Check.NotNull(updated!.NextRunUtc, "next run was scheduled forward");
                        }
                    }),

                    Case("SchedulerSkipsDisabledPolicy", "Scheduler advances a due schedule whose policy is disabled", async ct =>
                    {
                        using (TempWorkspace ws = new TempWorkspace())
                        using (ArmorContext context = await ArmorContext.CreateAsync(new ArmorPaths(ws.Combine("home")), ct).ConfigureAwait(false))
                        {
                            Policy policy = new Policy();
                            policy.Name = "disabled";
                            policy.Enabled = false;
                            policy.IncludePaths.Add(ws.Combine("src"));
                            await context.Database.Policies.CreateAsync(policy, ct).ConfigureAwait(false);

                            Schedule schedule = new Schedule();
                            schedule.PolicyId = policy.Id;
                            schedule.CronExpression = "*/5 * * * *";
                            schedule.NextRunUtc = DateTime.UtcNow.AddMinutes(-1);
                            await context.Database.Schedules.CreateAsync(schedule, ct).ConfigureAwait(false);

                            SchedulerService scheduler = new SchedulerService(context);
                            int ran = await scheduler.TickAsync(_ => Task.FromResult<byte[]?>(new byte[32]), DateTime.UtcNow, ct).ConfigureAwait(false);
                            Check.Equal(0, ran, "disabled policy is not run");
                        }
                    }),

                    Sync("DatabaseSettingsClamp", "Database settings clamp the busy timeout", () =>
                    {
                        DatabaseSettings settings = new DatabaseSettings("x.db");
                        settings.BusyTimeoutMilliseconds = 999999;
                        Check.Equal(120000, settings.BusyTimeoutMilliseconds, "busy timeout clamps high");
                        settings.BusyTimeoutMilliseconds = -5;
                        Check.Equal(0, settings.BusyTimeoutMilliseconds, "busy timeout clamps low");
                    })
                });
        }

        private static TestCaseDescriptor Case(string caseId, string displayName, Func<CancellationToken, Task> body)
        {
            return new TestCaseDescriptor(suiteId: "Coverage", caseId: caseId, displayName: displayName, executeAsync: body);
        }

        private static TestCaseDescriptor Sync(string caseId, string displayName, Action body)
        {
            return new TestCaseDescriptor(suiteId: "Coverage", caseId: caseId, displayName: displayName, executeAsync: _ =>
            {
                body();
                return Task.CompletedTask;
            });
        }

        private static async Task<DatabaseDriverBase> OpenAsync(string file, CancellationToken token)
        {
            return await DatabaseDriverFactory.CreateAndInitializeAsync(new DatabaseSettings(file), token).ConfigureAwait(false);
        }
    }
}
