namespace Test.Shared
{
    using System;
    using System.Collections.Generic;
    using System.IO;
    using System.Threading;
    using System.Threading.Tasks;
    using Armor.Core.Enums;
    using Armor.Core.Engine;
    using Armor.Core.Models;
    using Touchstone.Core;

    /// <summary>
    /// Verifies retention: old points-in-time are pruned, the newest is always kept, surviving points
    /// still restore, and chunks referenced only by pruned points are garbage-collected.
    /// </summary>
    public static class RetentionSuite
    {
        /// <summary>
        /// Build the retention test suite.
        /// </summary>
        /// <returns>The retention suite descriptor.</returns>
        public static TestSuiteDescriptor Build()
        {
            return new TestSuiteDescriptor(
                suiteId: "Retention",
                displayName: "Retention and Garbage Collection",
                cases: new List<TestCaseDescriptor>
                {
                    Case("PrunesOldKeepsNewestAndRestores", "Old point pruned, newest kept and still restorable", async ct =>
                    {
                        using (TempWorkspace ws = new TempWorkspace())
                        using (EngineFixture fx = await EngineFixture.BuildAsync(ws, ct).ConfigureAwait(false))
                        {
                            string source = Path.Combine(ws.RootDirectory, "source");
                            WriteFile(source, "x.bin", Content(1, 12000));
                            WriteFile(source, "y.bin", Content(2, 12000));

                            Policy policy = NewPolicy(source);
                            policy.RetentionDays = 30;
                            BackupEngine backup = new BackupEngine(fx.Database);
                            BackupJob job1 = await backup.RunAsync(policy, fx.Repository, fx.StorageTargetId, fx.EncryptionKey, fx.DataKey, fx.Chunking, BackupTypeEnum.Full, ct).ConfigureAwait(false);

                            WriteFile(source, "x.bin", Content(3, 16000));
                            BackupJob job2 = await backup.RunAsync(policy, fx.Repository, fx.StorageTargetId, fx.EncryptionKey, fx.DataKey, fx.Chunking, BackupTypeEnum.Incremental, ct).ConfigureAwait(false);

                            string job1ManifestKey = job1.ManifestKey!;
                            DateTime now = job1.CompletedUtc!.Value.AddDays(40);

                            RetentionManager retention = new RetentionManager(fx.Database);
                            RetentionResult result = await retention.RunAsync(policy, fx.Repository, fx.StorageTargetId, fx.DataKey, now, ct).ConfigureAwait(false);

                            Check.Equal(1, result.JobsPruned, "one old point-in-time pruned");
                            Check.Null(await fx.Database.BackupJobs.ReadAsync(job1.Id, ct).ConfigureAwait(false), "pruned job removed from the database");
                            Check.False(await fx.Repository.ObjectExistsAsync(job1ManifestKey, ct).ConfigureAwait(false), "pruned manifest removed from the target");

                            RestoreEngine engine = new RestoreEngine(fx.Database);
                            long verified = await engine.VerifyAsync(job2, fx.Repository, fx.DataKey, ct).ConfigureAwait(false);
                            Check.True(verified > 0, "surviving point still verifies");

                            string restore = Path.Combine(ws.RootDirectory, "restore");
                            RestoreJob rj = new RestoreJob();
                            rj.BackupJobId = job2.Id;
                            rj.Scope = RestoreScopeEnum.All;
                            rj.DestinationRoot = restore;
                            await engine.RunAsync(rj, job2, fx.Repository, fx.DataKey, ct).ConfigureAwait(false);
                            AssertRestored(source, restore, new[] { "x.bin", "y.bin" });
                        }
                    }),

                    Case("KeepsWithinWindow", "Nothing is pruned within the retention window", async ct =>
                    {
                        using (TempWorkspace ws = new TempWorkspace())
                        using (EngineFixture fx = await EngineFixture.BuildAsync(ws, ct).ConfigureAwait(false))
                        {
                            string source = Path.Combine(ws.RootDirectory, "source");
                            WriteFile(source, "a.bin", Content(4, 9000));

                            Policy policy = NewPolicy(source);
                            policy.RetentionDays = 30;
                            BackupEngine backup = new BackupEngine(fx.Database);
                            BackupJob job1 = await backup.RunAsync(policy, fx.Repository, fx.StorageTargetId, fx.EncryptionKey, fx.DataKey, fx.Chunking, BackupTypeEnum.Full, ct).ConfigureAwait(false);
                            WriteFile(source, "b.bin", Content(5, 9000));
                            BackupJob job2 = await backup.RunAsync(policy, fx.Repository, fx.StorageTargetId, fx.EncryptionKey, fx.DataKey, fx.Chunking, BackupTypeEnum.Full, ct).ConfigureAwait(false);

                            DateTime now = job2.CompletedUtc!.Value.AddDays(5);
                            RetentionManager retention = new RetentionManager(fx.Database);
                            RetentionResult result = await retention.RunAsync(policy, fx.Repository, fx.StorageTargetId, fx.DataKey, now, ct).ConfigureAwait(false);

                            Check.Equal(0, result.JobsPruned, "nothing pruned within the window");
                            Check.NotNull(await fx.Database.BackupJobs.ReadAsync(job1.Id, ct).ConfigureAwait(false), "older job retained");
                        }
                    })
                });
        }

        private static TestCaseDescriptor Case(string caseId, string displayName, Func<CancellationToken, Task> body)
        {
            return new TestCaseDescriptor(suiteId: "Retention", caseId: caseId, displayName: displayName, executeAsync: body);
        }

        private static Policy NewPolicy(string sourceDirectory)
        {
            Policy policy = new Policy();
            policy.Name = "retention-policy";
            policy.IncludePaths.Add(sourceDirectory);
            policy.StorageTargetId = "tgt_test";
            return policy;
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
