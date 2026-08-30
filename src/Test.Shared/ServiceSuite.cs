namespace Test.Shared
{
    using System;
    using System.Collections.Generic;
    using System.IO;
    using System.Threading;
    using System.Threading.Tasks;
    using Armor.Core.Configuration;
    using Armor.Core.Engine;
    using Armor.Core.Enums;
    using Armor.Core.Exceptions;
    using Armor.Core.Models;
    using Armor.Core.Scheduling;
    using Armor.Core.Security;
    using Armor.Core.Service;
    using Armor.Core.Storage;
    using Touchstone.Core;

    /// <summary>
    /// Verifies the service layer wiring: backup and restore through the services, storage-target
    /// credential protection at rest, and the scheduler running a due schedule.
    /// </summary>
    public static class ServiceSuite
    {
        /// <summary>
        /// Build the service test suite.
        /// </summary>
        /// <returns>The service suite descriptor.</returns>
        public static TestSuiteDescriptor Build()
        {
            return new TestSuiteDescriptor(
                suiteId: "Service",
                displayName: "Service Layer",
                cases: new List<TestCaseDescriptor>
                {
                    Case("BackupAndRestoreThroughServices", "Services back up and restore a policy", async ct =>
                    {
                        using (TempWorkspace ws = new TempWorkspace())
                        using (ArmorContext context = await ArmorContext.CreateAsync(new ArmorPaths(ws.Combine("home")), ct).ConfigureAwait(false))
                        {
                            SmallChunking(context);
                            EncryptionKeyService keyService = new EncryptionKeyService(context.Database);
                            ProvisionedKey provisioned = await keyService.ProvisionAsync("svc-key", "svc pass", null, 50000, ct).ConfigureAwait(false);

                            StorageTargetService targetService = new StorageTargetService(context.Database, context.CredentialProtector);
                            StorageTarget target = DiskTarget(ws.Combine("repo"));
                            await targetService.CreateAsync(target, ct).ConfigureAwait(false);

                            string source = ws.Combine("source");
                            WriteFile(source, "doc.txt", Content(1, 6000));
                            WriteFile(source, "sub/data.bin", Content(2, 9000));

                            Policy policy = new Policy();
                            policy.Name = "svc-policy";
                            policy.IncludePaths.Add(source);
                            policy.StorageTargetId = target.Id;
                            policy.EncryptionKeyId = provisioned.Key.Id;
                            await context.Database.Policies.CreateAsync(policy, ct).ConfigureAwait(false);

                            BackupService backupService = new BackupService(context);
                            BackupJob job = await backupService.RunAsync(policy.Id, provisioned.DataKey, BackupTypeEnum.Full, true, ct).ConfigureAwait(false);
                            Check.Equal(JobStatusEnum.Completed, job.Status, "service backup completed");

                            RestoreService restoreService = new RestoreService(context);
                            long verified = await restoreService.VerifyAsync(job.Id, provisioned.DataKey, ct).ConfigureAwait(false);
                            Check.True(verified > 0, "service verify checked chunks");

                            string restore = ws.Combine("restore");
                            RestoreJob rj = new RestoreJob();
                            rj.BackupJobId = job.Id;
                            rj.Scope = RestoreScopeEnum.All;
                            rj.DestinationRoot = restore;
                            await restoreService.RunAsync(rj, provisioned.DataKey, ct).ConfigureAwait(false);
                            AssertRestored(source, restore, new[] { "doc.txt", "sub/data.bin" });
                        }
                    }),

                    Case("FailedBackupResumesAndCompletes", "A failed backup resumes and completes with every file", async ct =>
                    {
                        using (TempWorkspace ws = new TempWorkspace())
                        using (ArmorContext context = await ArmorContext.CreateAsync(new ArmorPaths(ws.Combine("home")), ct).ConfigureAwait(false))
                        {
                            SmallChunking(context);
                            EncryptionKeyService keyService = new EncryptionKeyService(context.Database);
                            ProvisionedKey provisioned = await keyService.ProvisionAsync("resume-key", "pass", null, 50000, ct).ConfigureAwait(false);

                            StorageTargetService targetService = new StorageTargetService(context.Database, context.CredentialProtector);
                            StorageTarget target = DiskTarget(ws.Combine("repo"));
                            await targetService.CreateAsync(target, ct).ConfigureAwait(false);

                            // Six small, distinct single-chunk files so each is one chunk write.
                            string source = ws.Combine("source");
                            string[] names = { "a.bin", "b.bin", "c.bin", "d.bin", "e.bin", "f.bin" };
                            for (int i = 0; i < names.Length; i++)
                                WriteFile(source, names[i], Content(100 + i, 200));

                            Policy policy = new Policy();
                            policy.Name = "resume-policy";
                            policy.IncludePaths.Add(source);
                            policy.StorageTargetId = target.Id;
                            policy.EncryptionKeyId = provisioned.Key.Id;
                            await context.Database.Policies.CreateAsync(policy, ct).ConfigureAwait(false);

                            BackupEngine engine = new BackupEngine(context.Database);

                            // Run 1: the repository fails on the 4th chunk write, simulating a crash.
                            IStorageRepository realRepo = await targetService.BuildRepositoryAsync(target.Id, ct).ConfigureAwait(false);
                            FailingRepository failing = new FailingRepository(realRepo, 4);
                            bool threw = false;
                            try
                            {
                                await engine.RunAsync(policy, failing, target.Id, provisioned.Key, provisioned.DataKey, context.Settings.Chunking, BackupTypeEnum.Full, ct).ConfigureAwait(false);
                            }
                            catch (IOException)
                            {
                                threw = true;
                            }
                            Check.True(threw, "first run failed as simulated");

                            // The crashed run left a Failed job with work still pending.
                            BackupJob? crashed = null;
                            foreach (BackupJob j in await context.Database.BackupJobs.ReadByPolicyAsync(policy.Id, ct).ConfigureAwait(false))
                                if (j.Status == JobStatusEnum.Failed)
                                    crashed = j;
                            Check.NotNull(crashed, "a failed job was recorded");
                            Check.True(await context.Database.JobFiles.HasPendingAsync(crashed!.Id, ct).ConfigureAwait(false), "work remains pending after the crash");
                            JobFileTotals mid = await context.Database.JobFiles.ReadTotalsAsync(crashed.Id, ct).ConfigureAwait(false);
                            Check.True(mid.DoneCount > 0 && mid.DoneCount < names.Length, "the crash left the job partially done");

                            // Run 2: a healthy repository resumes the same job and finishes it.
                            IStorageRepository healthyRepo = await targetService.BuildRepositoryAsync(target.Id, ct).ConfigureAwait(false);
                            BackupJob resumed = await engine.RunAsync(policy, healthyRepo, target.Id, provisioned.Key, provisioned.DataKey, context.Settings.Chunking, BackupTypeEnum.Full, ct).ConfigureAwait(false);
                            Check.Equal(JobStatusEnum.Completed, resumed.Status, "resumed run completed");
                            Check.Equal(crashed.Id, resumed.Id, "the same job was resumed, not restarted");
                            Check.Equal((long)names.Length, resumed.FileCount, "every file is in the manifest");
                            Check.False(await context.Database.JobFiles.HasPendingAsync(resumed.Id, ct).ConfigureAwait(false), "work list cleared after completion");

                            // Every file restores correctly from the completed backup.
                            RestoreService restoreService = new RestoreService(context);
                            string restore = ws.Combine("restore");
                            RestoreJob rj = new RestoreJob();
                            rj.BackupJobId = resumed.Id;
                            rj.Scope = RestoreScopeEnum.All;
                            rj.DestinationRoot = restore;
                            await restoreService.RunAsync(rj, provisioned.DataKey, ct).ConfigureAwait(false);
                            AssertRestored(source, restore, names);
                        }
                    }),

                    Case("CanceledBackupIsNotResumed", "A canceled job with pending work is never resumed", async ct =>
                    {
                        using (TempWorkspace ws = new TempWorkspace())
                        using (ArmorContext context = await ArmorContext.CreateAsync(new ArmorPaths(ws.Combine("home")), ct).ConfigureAwait(false))
                        {
                            SmallChunking(context);
                            EncryptionKeyService keyService = new EncryptionKeyService(context.Database);
                            ProvisionedKey provisioned = await keyService.ProvisionAsync("cancel-key", "pass", null, 50000, ct).ConfigureAwait(false);

                            StorageTargetService targetService = new StorageTargetService(context.Database, context.CredentialProtector);
                            StorageTarget target = DiskTarget(ws.Combine("repo"));
                            await targetService.CreateAsync(target, ct).ConfigureAwait(false);

                            string source = ws.Combine("source");
                            WriteFile(source, "x.bin", Content(7, 300));
                            WriteFile(source, "y.bin", Content(8, 300));

                            Policy policy = new Policy();
                            policy.Name = "cancel-policy";
                            policy.IncludePaths.Add(source);
                            policy.StorageTargetId = target.Id;
                            policy.EncryptionKeyId = provisioned.Key.Id;
                            await context.Database.Policies.CreateAsync(policy, ct).ConfigureAwait(false);

                            // Seed a Canceled job that still has pending work — only Running/Failed jobs are
                            // resumable, so this one must be ignored.
                            BackupJob canceledJob = new BackupJob();
                            canceledJob.PolicyId = policy.Id;
                            canceledJob.BackupType = BackupTypeEnum.Full;
                            canceledJob.Status = JobStatusEnum.Canceled;
                            canceledJob.StartedUtc = DateTime.UtcNow;
                            await context.Database.BackupJobs.CreateAsync(canceledJob, ct).ConfigureAwait(false);

                            JobFileEntry seeded = new JobFileEntry();
                            seeded.Path = Path.Combine(source, "x.bin");
                            seeded.SizeBytes = 300;
                            await context.Database.JobFiles.AddPendingAsync(canceledJob.Id, new List<JobFileEntry> { seeded }, ct).ConfigureAwait(false);
                            Check.True(await context.Database.JobFiles.HasPendingAsync(canceledJob.Id, ct).ConfigureAwait(false), "seeded canceled job has pending work");

                            BackupEngine engine = new BackupEngine(context.Database);
                            IStorageRepository repo = await targetService.BuildRepositoryAsync(target.Id, ct).ConfigureAwait(false);
                            BackupJob fresh = await engine.RunAsync(policy, repo, target.Id, provisioned.Key, provisioned.DataKey, context.Settings.Chunking, BackupTypeEnum.Full, ct).ConfigureAwait(false);

                            Check.Equal(JobStatusEnum.Completed, fresh.Status, "fresh run completed");
                            Check.False(String.Equals(fresh.Id, canceledJob.Id, StringComparison.Ordinal), "a new job was started, not the canceled one resumed");
                            Check.Equal(2L, fresh.FileCount, "fresh run backed up both files");
                        }
                    }),

                    Case("StartupReconcilesInterruptedRunningJob", "Startup marks an orphaned running job as interrupted", async ct =>
                    {
                        using (TempWorkspace ws = new TempWorkspace())
                        using (ArmorContext context = await ArmorContext.CreateAsync(new ArmorPaths(ws.Combine("home")), ct).ConfigureAwait(false))
                        {
                            Policy policy = new Policy();
                            policy.Name = "interrupted-policy";
                            policy.IncludePaths.Add(ws.Combine("source"));
                            await context.Database.Policies.CreateAsync(policy, ct).ConfigureAwait(false);

                            // A job the prior process left Running because it exited mid-backup. No run lock is
                            // held now, so startup should flip it to Failed and leave its work list intact.
                            BackupJob orphan = new BackupJob();
                            orphan.PolicyId = policy.Id;
                            orphan.BackupType = BackupTypeEnum.Full;
                            orphan.Status = JobStatusEnum.Running;
                            orphan.StartedUtc = DateTime.UtcNow;
                            await context.Database.BackupJobs.CreateAsync(orphan, ct).ConfigureAwait(false);

                            JobFileEntry pending = new JobFileEntry();
                            pending.Path = Path.Combine(ws.Combine("source"), "z.bin");
                            pending.SizeBytes = 42;
                            await context.Database.JobFiles.AddPendingAsync(orphan.Id, new List<JobFileEntry> { pending }, ct).ConfigureAwait(false);

                            int reconciled = await new StartupMaintenance(context).ReconcileInterruptedBackupsAsync(ct).ConfigureAwait(false);
                            Check.Equal(1, reconciled, "one interrupted job reconciled");

                            BackupJob? after = await context.Database.BackupJobs.ReadAsync(orphan.Id, ct).ConfigureAwait(false);
                            Check.NotNull(after, "orphan job still present");
                            Check.Equal(JobStatusEnum.Failed, after!.Status, "orphan job marked failed");
                            Check.NotNull(after.CompletedUtc, "orphan job given a completion time");
                            Check.True(await context.Database.JobFiles.HasPendingAsync(orphan.Id, ct).ConfigureAwait(false), "work list preserved so the job stays resumable");
                        }
                    }),

                    Case("StartupLeavesLiveRunningJobAlone", "Startup leaves a job whose policy is locked alone", async ct =>
                    {
                        using (TempWorkspace ws = new TempWorkspace())
                        using (ArmorContext context = await ArmorContext.CreateAsync(new ArmorPaths(ws.Combine("home")), ct).ConfigureAwait(false))
                        {
                            Policy policy = new Policy();
                            policy.Name = "live-policy";
                            policy.IncludePaths.Add(ws.Combine("source"));
                            await context.Database.Policies.CreateAsync(policy, ct).ConfigureAwait(false);

                            BackupJob live = new BackupJob();
                            live.PolicyId = policy.Id;
                            live.BackupType = BackupTypeEnum.Full;
                            live.Status = JobStatusEnum.Running;
                            live.StartedUtc = DateTime.UtcNow;
                            await context.Database.BackupJobs.CreateAsync(live, ct).ConfigureAwait(false);

                            // Hold the policy's run lock to stand in for a live run in another process. Startup
                            // must not touch a job whose lock is held.
                            RunLock runLock = new RunLock(context.Paths.StateDirectory);
                            using (RunLockHandle? held = runLock.TryAcquire(policy.Id))
                            {
                                Check.NotNull(held, "acquired the run lock for the test");
                                int reconciled = await new StartupMaintenance(context).ReconcileInterruptedBackupsAsync(ct).ConfigureAwait(false);
                                Check.Equal(0, reconciled, "no job reconciled while the run lock is held");
                            }

                            BackupJob? after = await context.Database.BackupJobs.ReadAsync(live.Id, ct).ConfigureAwait(false);
                            Check.Equal(JobStatusEnum.Running, after!.Status, "live job left running");
                        }
                    }),

                    Case("CredentialProtectionAtRest", "Storage-target secrets are protected at rest", async ct =>
                    {
                        using (TempWorkspace ws = new TempWorkspace())
                        using (ArmorContext context = await ArmorContext.CreateAsync(new ArmorPaths(ws.Combine("home")), ct).ConfigureAwait(false))
                        {
                            StorageTargetService targetService = new StorageTargetService(context.Database, context.CredentialProtector);
                            StorageTarget target = new StorageTarget();
                            target.Name = "s3";
                            target.Type = StorageTargetTypeEnum.AmazonS3;
                            target.Region = "us-east-2";
                            target.Bucket = "b";
                            target.AccessKey = "AKIA";
                            target.SecretKey = "top-secret-value";
                            await targetService.CreateAsync(target, ct).ConfigureAwait(false);

                            StorageTarget? raw = await context.Database.StorageTargets.ReadAsync(target.Id, ct).ConfigureAwait(false);
                            Check.NotNull(raw, "raw target readable");
                            Check.False(String.Equals(raw!.SecretKey, "top-secret-value", StringComparison.Ordinal), "secret is not stored in plaintext");

                            StorageTarget? decrypted = await targetService.ReadDecryptedAsync(target.Id, ct).ConfigureAwait(false);
                            Check.Equal("top-secret-value", decrypted!.SecretKey, "secret decrypts back for use");
                        }
                    }),

                    Case("SchedulerRunsDueSchedule", "Scheduler runs a due schedule", async ct =>
                    {
                        using (TempWorkspace ws = new TempWorkspace())
                        using (ArmorContext context = await ArmorContext.CreateAsync(new ArmorPaths(ws.Combine("home")), ct).ConfigureAwait(false))
                        {
                            SmallChunking(context);
                            EncryptionKeyService keyService = new EncryptionKeyService(context.Database);
                            ProvisionedKey provisioned = await keyService.ProvisionAsync("sched-key", "pass", null, 50000, ct).ConfigureAwait(false);

                            StorageTargetService targetService = new StorageTargetService(context.Database, context.CredentialProtector);
                            StorageTarget target = DiskTarget(ws.Combine("repo"));
                            await targetService.CreateAsync(target, ct).ConfigureAwait(false);

                            string source = ws.Combine("source");
                            WriteFile(source, "file.txt", Content(3, 5000));

                            Policy policy = new Policy();
                            policy.Name = "sched-policy";
                            policy.IncludePaths.Add(source);
                            policy.StorageTargetId = target.Id;
                            policy.EncryptionKeyId = provisioned.Key.Id;
                            await context.Database.Policies.CreateAsync(policy, ct).ConfigureAwait(false);

                            Schedule schedule = new Schedule();
                            schedule.PolicyId = policy.Id;
                            schedule.CronExpression = "*/5 * * * *";
                            schedule.NextRunUtc = DateTime.UtcNow.AddMinutes(-1);
                            await context.Database.Schedules.CreateAsync(schedule, ct).ConfigureAwait(false);

                            SchedulerService scheduler = new SchedulerService(context);
                            int ran = await scheduler.TickAsync(_ => Task.FromResult<byte[]?>(provisioned.DataKey), DateTime.UtcNow, ct).ConfigureAwait(false);
                            Check.Equal(1, ran, "one schedule ran");

                            List<BackupJob> jobs = await context.Database.BackupJobs.ReadByPolicyAsync(policy.Id, ct).ConfigureAwait(false);
                            Check.Equal(1, jobs.Count, "a backup job was created by the scheduler");
                            Check.Equal(JobStatusEnum.Completed, jobs[0].Status, "scheduled backup completed");
                        }
                    }),

                    Case("BackupReportsProgress", "Backup reports progress to an observer", async ct =>
                    {
                        using (TempWorkspace ws = new TempWorkspace())
                        using (ArmorContext context = await ArmorContext.CreateAsync(new ArmorPaths(ws.Combine("home")), ct).ConfigureAwait(false))
                        {
                            SmallChunking(context);
                            EncryptionKeyService keyService = new EncryptionKeyService(context.Database);
                            ProvisionedKey provisioned = await keyService.ProvisionAsync("prog-key", "pw", null, 50000, ct).ConfigureAwait(false);

                            StorageTargetService targetService = new StorageTargetService(context.Database, context.CredentialProtector);
                            StorageTarget target = DiskTarget(ws.Combine("repo"));
                            await targetService.CreateAsync(target, ct).ConfigureAwait(false);

                            string source = ws.Combine("source");
                            WriteFile(source, "a.txt", Content(1, 6000));
                            WriteFile(source, "sub/b.bin", Content(2, 9000));
                            WriteFile(source, "sub/c.bin", Content(3, 4000));

                            Policy policy = new Policy();
                            policy.Name = "prog-policy";
                            policy.IncludePaths.Add(source);
                            policy.StorageTargetId = target.Id;
                            policy.EncryptionKeyId = provisioned.Key.Id;
                            await context.Database.Policies.CreateAsync(policy, ct).ConfigureAwait(false);

                            List<BackupProgress> reports = new List<BackupProgress>();
                            IProgress<BackupProgress> observer = new SyncProgress<BackupProgress>(reports.Add);

                            BackupService backupService = new BackupService(context);
                            BackupJob job = await backupService.RunAsync(policy.Id, provisioned.DataKey, BackupTypeEnum.Full, true, ct, observer).ConfigureAwait(false);

                            Check.True(reports.Count > 0, "progress was reported at least once");
                            BackupProgress last = reports[reports.Count - 1];
                            Check.Equal(3, last.FilesTotal, "pre-scan counted every file");
                            Check.Equal(last.FilesTotal, last.FilesDone, "final report shows all files done");
                            Check.Equal((int)job.FileCount, last.FilesDone, "progress agrees with the job's file count");
                            Check.True(last.BytesDone == last.BytesTotal && last.BytesTotal > 0, "final byte progress reached the total");
                        }
                    }),

                    Case("PurgeEmptiesTarget", "Purge removes all backup data from a target", async ct =>
                    {
                        using (TempWorkspace ws = new TempWorkspace())
                        using (ArmorContext context = await ArmorContext.CreateAsync(new ArmorPaths(ws.Combine("home")), ct).ConfigureAwait(false))
                        {
                            SmallChunking(context);
                            EncryptionKeyService keyService = new EncryptionKeyService(context.Database);
                            ProvisionedKey provisioned = await keyService.ProvisionAsync("purge-key", "pw", null, 50000, ct).ConfigureAwait(false);

                            StorageTargetService targetService = new StorageTargetService(context.Database, context.CredentialProtector);
                            StorageTarget target = DiskTarget(ws.Combine("repo"));
                            await targetService.CreateAsync(target, ct).ConfigureAwait(false);

                            string source = ws.Combine("source");
                            WriteFile(source, "doc.txt", Content(4, 7000));

                            Policy policy = new Policy();
                            policy.Name = "purge-policy";
                            policy.IncludePaths.Add(source);
                            policy.StorageTargetId = target.Id;
                            policy.EncryptionKeyId = provisioned.Key.Id;
                            await context.Database.Policies.CreateAsync(policy, ct).ConfigureAwait(false);

                            BackupService backupService = new BackupService(context);
                            await backupService.RunAsync(policy.Id, provisioned.DataKey, BackupTypeEnum.Full, true, ct).ConfigureAwait(false);

                            int deleted = await targetService.PurgeAsync(target.Id, ct).ConfigureAwait(false);
                            Check.True(deleted > 0, "purge deleted the repository objects");

                            // After a purge the repository header is gone, so recovery finds nothing.
                            bool recoveryFailed = false;
                            try
                            {
                                await new RecoveryService(context).OpenAsync(target.Id, "pw", ct).ConfigureAwait(false);
                            }
                            catch (ArmorException)
                            {
                                recoveryFailed = true;
                            }
                            Check.True(recoveryFailed, "no repository remains after purge");

                            // Purging a target that does not exist is an error.
                            bool missingThrew = false;
                            try
                            {
                                await targetService.PurgeAsync("tgt_does_not_exist", ct).ConfigureAwait(false);
                            }
                            catch (ArmorException)
                            {
                                missingThrew = true;
                            }
                            Check.True(missingThrew, "purging a missing target throws");
                        }
                    }),

                    Case("RecoverFromTargetWithPassword", "Recovery reads and restores from a target with the password", async ct =>
                    {
                        using (TempWorkspace ws = new TempWorkspace())
                        using (ArmorContext context = await ArmorContext.CreateAsync(new ArmorPaths(ws.Combine("home")), ct).ConfigureAwait(false))
                        {
                            SmallChunking(context);
                            EncryptionKeyService keyService = new EncryptionKeyService(context.Database);
                            ProvisionedKey provisioned = await keyService.ProvisionAsync("rec-key", "the-pass", null, 50000, ct).ConfigureAwait(false);

                            StorageTargetService targetService = new StorageTargetService(context.Database, context.CredentialProtector);
                            StorageTarget target = DiskTarget(ws.Combine("repo"));
                            await targetService.CreateAsync(target, ct).ConfigureAwait(false);

                            string source = ws.Combine("source");
                            WriteFile(source, "keep.txt", Content(5, 6000));
                            WriteFile(source, "sub/deep.bin", Content(6, 8000));

                            Policy policy = new Policy();
                            policy.Name = "rec-policy";
                            policy.IncludePaths.Add(source);
                            policy.StorageTargetId = target.Id;
                            policy.EncryptionKeyId = provisioned.Key.Id;
                            await context.Database.Policies.CreateAsync(policy, ct).ConfigureAwait(false);

                            BackupService backupService = new BackupService(context);
                            await backupService.RunAsync(policy.Id, provisioned.DataKey, BackupTypeEnum.Full, true, ct).ConfigureAwait(false);

                            RecoveryService recovery = new RecoveryService(context);

                            // Wrong password is rejected.
                            bool wrongRejected = false;
                            try
                            {
                                await recovery.OpenAsync(target.Id, "not-the-pass", ct).ConfigureAwait(false);
                            }
                            catch (ArmorCryptoException)
                            {
                                wrongRejected = true;
                            }
                            Check.True(wrongRejected, "wrong password is rejected");

                            // Right password opens, browses, and restores.
                            RecoverySession session = await recovery.OpenAsync(target.Id, "the-pass", ct).ConfigureAwait(false);
                            List<RecoveryPoint> points = await session.BrowseAsync(ct).ConfigureAwait(false);
                            Check.Equal(1, points.Count, "one recovery point found on the target");
                            Check.Equal(2, (int)points[0].FileCount, "the recovery point lists both files");

                            string restore = ws.Combine("recovered");
                            RestoreJob rj = new RestoreJob();
                            rj.Scope = RestoreScopeEnum.All;
                            rj.DestinationRoot = restore;
                            RestoreJob done = await session.RestoreAsync(points[0], rj, ct).ConfigureAwait(false);
                            Check.Equal(JobStatusEnum.Completed, done.Status, "recovery restore completed");
                            AssertRestored(source, restore, new[] { "keep.txt", "sub/deep.bin" });
                        }
                    }),

                    Case("DisconnectedTargetFailsLoudly", "A backup fails when the target repository has vanished (drive unplugged)", async ct =>
                    {
                        using (TempWorkspace ws = new TempWorkspace())
                        using (ArmorContext context = await ArmorContext.CreateAsync(new ArmorPaths(ws.Combine("home")), ct).ConfigureAwait(false))
                        {
                            SmallChunking(context);
                            EncryptionKeyService keyService = new EncryptionKeyService(context.Database);
                            ProvisionedKey provisioned = await keyService.ProvisionAsync("disc-key", "pw", null, 50000, ct).ConfigureAwait(false);

                            StorageTargetService targetService = new StorageTargetService(context.Database, context.CredentialProtector);
                            StorageTarget target = DiskTarget(ws.Combine("repo"));
                            await targetService.CreateAsync(target, ct).ConfigureAwait(false);

                            string src = ws.Combine("source");
                            WriteFile(src, "a.txt", Content(8, 5000));

                            Policy policy = new Policy();
                            policy.Name = "disc-policy";
                            policy.IncludePaths.Add(src);
                            policy.StorageTargetId = target.Id;
                            policy.EncryptionKeyId = provisioned.Key.Id;
                            await context.Database.Policies.CreateAsync(policy, ct).ConfigureAwait(false);

                            BackupService backupService = new BackupService(context);
                            BackupJob first = await backupService.RunAsync(policy.Id, provisioned.DataKey, BackupTypeEnum.Full, true, ct).ConfigureAwait(false);
                            Check.Equal(JobStatusEnum.Completed, first.Status, "first backup completed");

                            // Simulate the drive going away: the repository header is no longer present.
                            IStorageRepository repo = await targetService.BuildRepositoryAsync(target.Id, ct).ConfigureAwait(false);
                            await repo.DeleteObjectAsync(RepositoryKeys.HeaderKey, ct).ConfigureAwait(false);

                            bool threw = false;
                            try
                            {
                                await backupService.RunAsync(policy.Id, provisioned.DataKey, BackupTypeEnum.Full, true, ct).ConfigureAwait(false);
                            }
                            catch (Armor.Core.Exceptions.ArmorException)
                            {
                                threw = true;
                            }
                            Check.True(threw, "a second backup must refuse when the repository is gone but prior backups exist");
                        }
                    }),

                    Case("IncrementalRestoresUnchangedFileFromFull", "Restoring a recent incremental recovers a file only ever chunked in the full", async ct =>
                    {
                        using (TempWorkspace ws = new TempWorkspace())
                        using (ArmorContext context = await ArmorContext.CreateAsync(new ArmorPaths(ws.Combine("home")), ct).ConfigureAwait(false))
                        {
                            SmallChunking(context);
                            EncryptionKeyService keyService = new EncryptionKeyService(context.Database);
                            ProvisionedKey provisioned = await keyService.ProvisionAsync("inc-key", "pw", null, 50000, ct).ConfigureAwait(false);
                            StorageTargetService targetService = new StorageTargetService(context.Database, context.CredentialProtector);
                            StorageTarget target = DiskTarget(ws.Combine("repo"));
                            await targetService.CreateAsync(target, ct).ConfigureAwait(false);

                            string source = ws.Combine("source");
                            byte[] keepContent = Content(1, 4000);
                            WriteFile(source, "keep.txt", keepContent);   // never changes after the full
                            WriteFile(source, "r1.txt", Content(2, 1000));
                            WriteFile(source, "r2.txt", Content(3, 1000));
                            WriteFile(source, "r3.txt", Content(4, 1000));

                            Policy policy = new Policy();
                            policy.Name = "inc-policy";
                            policy.IncludePaths.Add(source);
                            policy.StorageTargetId = target.Id;
                            policy.EncryptionKeyId = provisioned.Key.Id;
                            await context.Database.Policies.CreateAsync(policy, ct).ConfigureAwait(false);

                            BackupService backupService = new BackupService(context);

                            // Full backup: this is the only run that chunks keep.txt.
                            await backupService.RunAsync(policy.Id, provisioned.DataKey, BackupTypeEnum.Full, true, ct).ConfigureAwait(false);

                            // Three incrementals, each changing a different file (by size, to force detection).
                            // keep.txt is never touched again.
                            WriteFile(source, "r1.txt", Content(12, 2000));
                            await backupService.RunAsync(policy.Id, provisioned.DataKey, BackupTypeEnum.Incremental, true, ct).ConfigureAwait(false);
                            WriteFile(source, "r2.txt", Content(13, 3000));
                            await backupService.RunAsync(policy.Id, provisioned.DataKey, BackupTypeEnum.Incremental, true, ct).ConfigureAwait(false);
                            WriteFile(source, "r3.txt", Content(14, 5000));
                            BackupJob last = await backupService.RunAsync(policy.Id, provisioned.DataKey, BackupTypeEnum.Incremental, true, ct).ConfigureAwait(false);
                            Check.Equal(JobStatusEnum.Completed, last.Status, "final incremental completed");
                            Check.Equal(4L, last.FileCount, "the incremental manifest still lists every file, not just changed ones");

                            // Restore from the most recent incremental — keep.txt, chunked three backups ago
                            // in the full, must come back byte-for-byte.
                            RestoreService restoreService = new RestoreService(context);
                            string restore = ws.Combine("restore");
                            RestoreJob rj = new RestoreJob();
                            rj.BackupJobId = last.Id;
                            rj.Scope = RestoreScopeEnum.All;
                            rj.DestinationRoot = restore;
                            await restoreService.RunAsync(rj, provisioned.DataKey, ct).ConfigureAwait(false);

                            AssertRestored(source, restore, new[] { "keep.txt", "r1.txt", "r2.txt", "r3.txt" });
                        }
                    }),

                    Case("UnreadableFileIsSkippedNotFatal", "A file that cannot be read is skipped and the backup still completes", async ct =>
                    {
                        using (TempWorkspace ws = new TempWorkspace())
                        using (ArmorContext context = await ArmorContext.CreateAsync(new ArmorPaths(ws.Combine("home")), ct).ConfigureAwait(false))
                        {
                            SmallChunking(context);
                            EncryptionKeyService keyService = new EncryptionKeyService(context.Database);
                            ProvisionedKey provisioned = await keyService.ProvisionAsync("skip-key", "pw", null, 50000, ct).ConfigureAwait(false);
                            StorageTargetService targetService = new StorageTargetService(context.Database, context.CredentialProtector);
                            StorageTarget target = DiskTarget(ws.Combine("repo"));
                            await targetService.CreateAsync(target, ct).ConfigureAwait(false);

                            string source = ws.Combine("source");
                            WriteFile(source, "good.txt", Content(1, 2000));
                            WriteFile(source, "locked.txt", Content(2, 2000));

                            Policy policy = new Policy();
                            policy.Name = "skip-policy";
                            policy.IncludePaths.Add(source);
                            policy.StorageTargetId = target.Id;
                            policy.EncryptionKeyId = provisioned.Key.Id;
                            await context.Database.Policies.CreateAsync(policy, ct).ConfigureAwait(false);

                            BackupService backupService = new BackupService(context);

                            // Hold an exclusive lock (deny all sharing) on one file so the engine cannot open it.
                            BackupJob job;
                            string lockedPath = Path.Combine(source, "locked.txt");
                            using (FileStream hold = new FileStream(lockedPath, FileMode.Open, FileAccess.Read, FileShare.None))
                            {
                                job = await backupService.RunAsync(policy.Id, provisioned.DataKey, BackupTypeEnum.Full, true, ct).ConfigureAwait(false);
                                Check.Equal(JobStatusEnum.Completed, job.Status, "backup completed despite an unreadable file");
                                Check.Equal(1L, job.FileCount, "only the readable file is in the manifest");
                            }

                            // The readable file restores correctly from the completed backup.
                            RestoreService restoreService = new RestoreService(context);
                            string restore = ws.Combine("restore");
                            RestoreJob rj = new RestoreJob();
                            rj.BackupJobId = job.Id;
                            rj.Scope = RestoreScopeEnum.All;
                            rj.DestinationRoot = restore;
                            await restoreService.RunAsync(rj, provisioned.DataKey, ct).ConfigureAwait(false);
                            AssertRestored(source, restore, new[] { "good.txt" });
                        }
                    }),

                    Case("SchedulerIsolatesFailingSchedule", "One unreachable target does not stop the other schedules", async ct =>
                    {
                        using (TempWorkspace ws = new TempWorkspace())
                        using (ArmorContext context = await ArmorContext.CreateAsync(new ArmorPaths(ws.Combine("home")), ct).ConfigureAwait(false))
                        {
                            SmallChunking(context);
                            EncryptionKeyService keyService = new EncryptionKeyService(context.Database);
                            ProvisionedKey provisioned = await keyService.ProvisionAsync("sch-key", "pw", null, 50000, ct).ConfigureAwait(false);
                            StorageTargetService targetService = new StorageTargetService(context.Database, context.CredentialProtector);
                            BackupService backupService = new BackupService(context);

                            // A target that will fail: back it up once, then remove its header.
                            StorageTarget badTarget = new StorageTarget { Name = "bad", Type = StorageTargetTypeEnum.Disk, DiskPath = ws.Combine("bad-repo") };
                            await targetService.CreateAsync(badTarget, ct).ConfigureAwait(false);
                            string badSrc = ws.Combine("bad-src");
                            WriteFile(badSrc, "x.txt", Content(9, 4000));
                            Policy badPolicy = new Policy { Name = "bad-policy" };
                            badPolicy.IncludePaths.Add(badSrc);
                            badPolicy.StorageTargetId = badTarget.Id;
                            badPolicy.EncryptionKeyId = provisioned.Key.Id;
                            await context.Database.Policies.CreateAsync(badPolicy, ct).ConfigureAwait(false);
                            await backupService.RunAsync(badPolicy.Id, provisioned.DataKey, BackupTypeEnum.Full, true, ct).ConfigureAwait(false);
                            IStorageRepository badRepo = await targetService.BuildRepositoryAsync(badTarget.Id, ct).ConfigureAwait(false);
                            await badRepo.DeleteObjectAsync(RepositoryKeys.HeaderKey, ct).ConfigureAwait(false);

                            // A healthy target that should still run.
                            StorageTarget goodTarget = new StorageTarget { Name = "good", Type = StorageTargetTypeEnum.Disk, DiskPath = ws.Combine("good-repo") };
                            await targetService.CreateAsync(goodTarget, ct).ConfigureAwait(false);
                            string goodSrc = ws.Combine("good-src");
                            WriteFile(goodSrc, "y.txt", Content(10, 4000));
                            Policy goodPolicy = new Policy { Name = "good-policy" };
                            goodPolicy.IncludePaths.Add(goodSrc);
                            goodPolicy.StorageTargetId = goodTarget.Id;
                            goodPolicy.EncryptionKeyId = provisioned.Key.Id;
                            await context.Database.Policies.CreateAsync(goodPolicy, ct).ConfigureAwait(false);

                            // Both schedules due; the bad one is created first so it is processed first.
                            Schedule badSchedule = new Schedule { PolicyId = badPolicy.Id, CronExpression = "*/5 * * * *", NextRunUtc = DateTime.UtcNow.AddMinutes(-1) };
                            await context.Database.Schedules.CreateAsync(badSchedule, ct).ConfigureAwait(false);
                            Schedule goodSchedule = new Schedule { PolicyId = goodPolicy.Id, CronExpression = "*/5 * * * *", NextRunUtc = DateTime.UtcNow.AddMinutes(-1) };
                            await context.Database.Schedules.CreateAsync(goodSchedule, ct).ConfigureAwait(false);

                            int errors = 0;
                            SchedulerService scheduler = new SchedulerService(context);
                            int ran = await scheduler.TickAsync(_ => Task.FromResult<byte[]?>(provisioned.DataKey), DateTime.UtcNow, ct, (_, __) => errors++).ConfigureAwait(false);

                            Check.Equal(1, ran, "the healthy schedule ran despite the failing one");
                            Check.True(errors >= 1, "the failing schedule reported an error");

                            List<BackupJob> goodJobs = await context.Database.BackupJobs.ReadByPolicyAsync(goodPolicy.Id, ct).ConfigureAwait(false);
                            Check.True(goodJobs.Count >= 1 && goodJobs[goodJobs.Count - 1].Status == JobStatusEnum.Completed, "the healthy policy produced a completed backup");
                        }
                    }),

                    Case("SchedulerSkipsUnreachableRemovableTarget", "An offline removable target is skipped (not failed) and left due, while other schedules run", async ct =>
                    {
                        // A removable/USB target that is not connected must not be recorded as a failure on
                        // every tick, and must not block a reachable target's schedule (for example an S3
                        // policy). The absent volume is a Windows drive-letter concept, so this is asserted
                        // there; on other platforms a drive letter has no root to probe and the case is a no-op.
                        if (!OperatingSystem.IsWindows())
                            return;

                        HashSet<char> mounted = new HashSet<char>();
                        foreach (DriveInfo d in DriveInfo.GetDrives())
                            if (d.Name.Length > 0)
                                mounted.Add(Char.ToUpperInvariant(d.Name[0]));
                        char? spare = null;
                        for (char c = 'Z'; c >= 'E'; c--)
                        {
                            if (!mounted.Contains(c)) { spare = c; break; }
                        }
                        if (spare == null)
                            return; // No unmounted drive letter available to stand in for an unplugged drive.

                        using (TempWorkspace ws = new TempWorkspace())
                        using (ArmorContext context = await ArmorContext.CreateAsync(new ArmorPaths(ws.Combine("home")), ct).ConfigureAwait(false))
                        {
                            SmallChunking(context);
                            EncryptionKeyService keyService = new EncryptionKeyService(context.Database);
                            ProvisionedKey provisioned = await keyService.ProvisionAsync("sch-key", "pw", null, 50000, ct).ConfigureAwait(false);
                            StorageTargetService targetService = new StorageTargetService(context.Database, context.CredentialProtector);

                            // Offline removable target: its drive letter is not mounted.
                            StorageTarget offline = new StorageTarget { Name = "usb", Type = StorageTargetTypeEnum.Disk, DiskPath = spare.Value + ":\\Armor" };
                            await targetService.CreateAsync(offline, ct).ConfigureAwait(false);
                            string offlineSrc = ws.Combine("usb-src");
                            WriteFile(offlineSrc, "a.txt", Content(11, 4000));
                            Policy offlinePolicy = new Policy { Name = "usb-policy" };
                            offlinePolicy.IncludePaths.Add(offlineSrc);
                            offlinePolicy.StorageTargetId = offline.Id;
                            offlinePolicy.EncryptionKeyId = provisioned.Key.Id;
                            await context.Database.Policies.CreateAsync(offlinePolicy, ct).ConfigureAwait(false);

                            // A reachable target that must still run.
                            StorageTarget good = new StorageTarget { Name = "good", Type = StorageTargetTypeEnum.Disk, DiskPath = ws.Combine("good-repo") };
                            await targetService.CreateAsync(good, ct).ConfigureAwait(false);
                            string goodSrc = ws.Combine("good-src");
                            WriteFile(goodSrc, "b.txt", Content(12, 4000));
                            Policy goodPolicy = new Policy { Name = "good-policy" };
                            goodPolicy.IncludePaths.Add(goodSrc);
                            goodPolicy.StorageTargetId = good.Id;
                            goodPolicy.EncryptionKeyId = provisioned.Key.Id;
                            await context.Database.Policies.CreateAsync(goodPolicy, ct).ConfigureAwait(false);

                            DateTime due = DateTime.UtcNow.AddMinutes(-1);
                            Schedule offlineSchedule = new Schedule { PolicyId = offlinePolicy.Id, CronExpression = "*/5 * * * *", NextRunUtc = due };
                            await context.Database.Schedules.CreateAsync(offlineSchedule, ct).ConfigureAwait(false);
                            Schedule goodSchedule = new Schedule { PolicyId = goodPolicy.Id, CronExpression = "*/5 * * * *", NextRunUtc = due };
                            await context.Database.Schedules.CreateAsync(goodSchedule, ct).ConfigureAwait(false);

                            int errors = 0;
                            SchedulerService scheduler = new SchedulerService(context);
                            int ran = await scheduler.TickAsync(_ => Task.FromResult<byte[]?>(provisioned.DataKey), DateTime.UtcNow, ct, (_, __) => errors++).ConfigureAwait(false);

                            Check.Equal(1, ran, "the reachable schedule ran despite the offline one");
                            Check.Equal(0, errors, "the offline removable target reported no failure");

                            // The offline schedule was left due (not advanced), so it retries once the drive returns.
                            Schedule reloaded = (await context.Database.Schedules.ReadAsync(offlineSchedule.Id, ct).ConfigureAwait(false))!;
                            Check.True(reloaded.NextRunUtc.HasValue && reloaded.NextRunUtc.Value <= DateTime.UtcNow, "the offline schedule remained due");

                            // No job row was created at all — not even a failed one — for the offline target.
                            List<BackupJob> offlineJobs = await context.Database.BackupJobs.ReadByPolicyAsync(offlinePolicy.Id, ct).ConfigureAwait(false);
                            Check.Equal(0, offlineJobs.Count, "no job row was created for the offline target");
                        }
                    })
                });
        }

        private sealed class SyncProgress<T> : IProgress<T>
        {
            private readonly Action<T> _handler;

            public SyncProgress(Action<T> handler)
            {
                _handler = handler;
            }

            public void Report(T value)
            {
                _handler(value);
            }
        }

        private static TestCaseDescriptor Case(string caseId, string displayName, Func<CancellationToken, Task> body)
        {
            return new TestCaseDescriptor(suiteId: "Service", caseId: caseId, displayName: displayName, executeAsync: body);
        }

        private static void SmallChunking(ArmorContext context)
        {
            context.Settings.Chunking.MinSizeBytes = 1024;
            context.Settings.Chunking.AvgSizeBytes = 2048;
            context.Settings.Chunking.MaxSizeBytes = 8192;
        }

        private static StorageTarget DiskTarget(string diskPath)
        {
            StorageTarget target = new StorageTarget();
            target.Name = "svc-disk";
            target.Type = StorageTargetTypeEnum.Disk;
            target.DiskPath = diskPath;
            return target;
        }

        private static void WriteFile(string root, string relative, byte[] content)
        {
            string full = Path.Combine(root, relative.Replace('/', Path.DirectorySeparatorChar));
            string? directory = Path.GetDirectoryName(full);
            if (!String.IsNullOrEmpty(directory))
                Directory.CreateDirectory(directory);
            File.WriteAllBytes(full, content);
        }

        private static void AssertRestored(string sourceRoot, string restoreRoot, string[] relativePaths)
        {
            foreach (string relative in relativePaths)
            {
                string sourcePath = Path.Combine(sourceRoot, relative.Replace('/', Path.DirectorySeparatorChar));
                string root = Path.GetPathRoot(sourcePath) ?? String.Empty;
                string rel = root.Length > 0 && sourcePath.StartsWith(root, StringComparison.Ordinal) ? sourcePath.Substring(root.Length) : sourcePath;
                rel = rel.TrimStart('/', '\\');
                string restoredPath = Path.Combine(restoreRoot, rel);
                Check.True(File.Exists(restoredPath), "restored file exists: " + relative);
                Check.True(Equal(File.ReadAllBytes(sourcePath), File.ReadAllBytes(restoredPath)), "restored byte-identical: " + relative);
            }
        }

        private static byte[] Content(int seed, int length)
        {
            byte[] data = new byte[length];
            ulong state = (ulong)(seed * 2654435761U) + 0x9E3779B97F4A7C15UL;
            for (int i = 0; i < length; i++)
            {
                state ^= state << 13;
                state ^= state >> 7;
                state ^= state << 17;
                data[i] = (byte)(state & 0xFF);
            }
            return data;
        }

        private static bool Equal(byte[] a, byte[] b)
        {
            if (a.Length != b.Length)
                return false;
            for (int i = 0; i < a.Length; i++)
                if (a[i] != b[i])
                    return false;
            return true;
        }
    }
}
