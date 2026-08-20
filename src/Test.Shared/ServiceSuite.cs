namespace Test.Shared
{
    using System;
    using System.Collections.Generic;
    using System.IO;
    using System.Threading;
    using System.Threading.Tasks;
    using Armor.Core.Configuration;
    using Armor.Core.Enums;
    using Armor.Core.Models;
    using Armor.Core.Security;
    using Armor.Core.Service;
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
                    })
                });
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
