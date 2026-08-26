namespace Test.Shared
{
    using System;
    using System.Collections.Generic;
    using System.IO;
    using System.Text;
    using System.Threading;
    using System.Threading.Tasks;
    using Armor.Core.Enums;
    using Armor.Core.Exceptions;
    using Armor.Core.Engine;
    using Armor.Core.Models;
    using Touchstone.Core;

    /// <summary>
    /// Verifies the backup and restore engines end-to-end: full/incremental/differential backups,
    /// byte-identical restores (whole tree, folder, and single file), deduplication, exclude patterns
    /// and size bounds, empty files, verification, and loud failure on corruption.
    /// </summary>
    public static class EngineSuite
    {
        /// <summary>
        /// Build the engine test suite.
        /// </summary>
        /// <returns>The engine suite descriptor.</returns>
        public static TestSuiteDescriptor Build()
        {
            return new TestSuiteDescriptor(
                suiteId: "Engine",
                displayName: "Backup and Restore Engines",
                cases: new List<TestCaseDescriptor>
                {
                    Case("FullBackupRestore", "Full backup restores byte-identically", async ct =>
                    {
                        using (TempWorkspace ws = new TempWorkspace())
                        using (EngineFixture fx = await EngineFixture.BuildAsync(ws, ct).ConfigureAwait(false))
                        {
                            string source = Path.Combine(ws.RootDirectory, "source");
                            WriteFile(source, "a.txt", Content(1, 5000));
                            WriteFile(source, "sub/b.bin", Content(2, 12000));
                            WriteFile(source, "sub/deep/c.dat", Content(3, 300));

                            Policy policy = NewPolicy(source);
                            BackupEngine backup = new BackupEngine(fx.Database);
                            BackupJob job = await backup.RunAsync(policy, fx.Repository, fx.StorageTargetId, fx.EncryptionKey, fx.DataKey, fx.Chunking, BackupTypeEnum.Full, ct).ConfigureAwait(false);
                            Check.Equal(JobStatusEnum.Completed, job.Status, "backup completed");
                            Check.Equal(3L, job.FileCount, "three files captured");

                            string restore = Path.Combine(ws.RootDirectory, "restore");
                            await RestoreAllAsync(fx, job, restore, ct).ConfigureAwait(false);
                            AssertRestored(source, restore, new[] { "a.txt", "sub/b.bin", "sub/deep/c.dat" });
                        }
                    }),

                    Case("IncrementalCapturesChanges", "Incremental restores the current tree", async ct =>
                    {
                        using (TempWorkspace ws = new TempWorkspace())
                        using (EngineFixture fx = await EngineFixture.BuildAsync(ws, ct).ConfigureAwait(false))
                        {
                            string source = Path.Combine(ws.RootDirectory, "source");
                            WriteFile(source, "keep.txt", Content(10, 4000));
                            WriteFile(source, "change.txt", Content(11, 4000));

                            Policy policy = NewPolicy(source);
                            BackupEngine backup = new BackupEngine(fx.Database);
                            await backup.RunAsync(policy, fx.Repository, fx.StorageTargetId, fx.EncryptionKey, fx.DataKey, fx.Chunking, BackupTypeEnum.Full, ct).ConfigureAwait(false);

                            WriteFile(source, "change.txt", Content(12, 6000));
                            WriteFile(source, "added.txt", Content(13, 2000));

                            BackupJob incr = await backup.RunAsync(policy, fx.Repository, fx.StorageTargetId, fx.EncryptionKey, fx.DataKey, fx.Chunking, BackupTypeEnum.Incremental, ct).ConfigureAwait(false);
                            Check.Equal(3L, incr.FileCount, "incremental manifest lists all current files");
                            Check.True(incr.ChunksReused > 0, "unchanged file reused chunks");

                            string restore = Path.Combine(ws.RootDirectory, "restore");
                            await RestoreAllAsync(fx, incr, restore, ct).ConfigureAwait(false);
                            AssertRestored(source, restore, new[] { "keep.txt", "change.txt", "added.txt" });
                        }
                    }),

                    Case("DifferentialAgainstFull", "Differential restores the current tree", async ct =>
                    {
                        using (TempWorkspace ws = new TempWorkspace())
                        using (EngineFixture fx = await EngineFixture.BuildAsync(ws, ct).ConfigureAwait(false))
                        {
                            string source = Path.Combine(ws.RootDirectory, "source");
                            WriteFile(source, "one.txt", Content(20, 4000));
                            WriteFile(source, "two.txt", Content(21, 4000));

                            Policy policy = NewPolicy(source);
                            BackupEngine backup = new BackupEngine(fx.Database);
                            await backup.RunAsync(policy, fx.Repository, fx.StorageTargetId, fx.EncryptionKey, fx.DataKey, fx.Chunking, BackupTypeEnum.Full, ct).ConfigureAwait(false);

                            WriteFile(source, "two.txt", Content(22, 8000));
                            BackupJob diff = await backup.RunAsync(policy, fx.Repository, fx.StorageTargetId, fx.EncryptionKey, fx.DataKey, fx.Chunking, BackupTypeEnum.Differential, ct).ConfigureAwait(false);

                            string restore = Path.Combine(ws.RootDirectory, "restore");
                            await RestoreAllAsync(fx, diff, restore, ct).ConfigureAwait(false);
                            AssertRestored(source, restore, new[] { "one.txt", "two.txt" });
                        }
                    }),

                    Case("DeduplicationAcrossFiles", "Identical files deduplicate", async ct =>
                    {
                        using (TempWorkspace ws = new TempWorkspace())
                        using (EngineFixture fx = await EngineFixture.BuildAsync(ws, ct).ConfigureAwait(false))
                        {
                            string source = Path.Combine(ws.RootDirectory, "source");
                            byte[] shared = Content(30, 20000);
                            WriteFile(source, "first.bin", shared);
                            WriteFile(source, "second.bin", shared);

                            Policy policy = NewPolicy(source);
                            BackupEngine backup = new BackupEngine(fx.Database);
                            BackupJob job = await backup.RunAsync(policy, fx.Repository, fx.StorageTargetId, fx.EncryptionKey, fx.DataKey, fx.Chunking, BackupTypeEnum.Full, ct).ConfigureAwait(false);

                            Check.True(job.ChunksReused > 0, "identical second file reused chunks");
                            Check.True(job.ChunksWritten > 0, "unique chunks were written");
                        }
                    }),

                    Case("RestoreSingleFile", "Restoring a single file writes only that file", async ct =>
                    {
                        using (TempWorkspace ws = new TempWorkspace())
                        using (EngineFixture fx = await EngineFixture.BuildAsync(ws, ct).ConfigureAwait(false))
                        {
                            string source = Path.Combine(ws.RootDirectory, "source");
                            WriteFile(source, "x.txt", Content(40, 3000));
                            WriteFile(source, "y.txt", Content(41, 3000));

                            Policy policy = NewPolicy(source);
                            BackupEngine backup = new BackupEngine(fx.Database);
                            BackupJob job = await backup.RunAsync(policy, fx.Repository, fx.StorageTargetId, fx.EncryptionKey, fx.DataKey, fx.Chunking, BackupTypeEnum.Full, ct).ConfigureAwait(false);

                            string restore = Path.Combine(ws.RootDirectory, "restore");
                            RestoreJob rj = new RestoreJob();
                            rj.BackupJobId = job.Id;
                            rj.Scope = RestoreScopeEnum.File;
                            rj.SourceSelector = Path.Combine(source, "x.txt");
                            rj.DestinationRoot = restore;
                            RestoreEngine engine = new RestoreEngine(fx.Database);
                            RestoreJob done = await engine.RunAsync(rj, job, fx.Repository, fx.DataKey, ct).ConfigureAwait(false);

                            Check.Equal(1L, done.FilesRestored, "only one file restored");
                            AssertRestored(source, restore, new[] { "x.txt" });
                            Check.False(File.Exists(MappedPath(restore, Path.Combine(source, "y.txt"))), "other file was not restored");
                        }
                    }),

                    Case("RestoreFolder", "Restoring a folder writes only its subtree", async ct =>
                    {
                        using (TempWorkspace ws = new TempWorkspace())
                        using (EngineFixture fx = await EngineFixture.BuildAsync(ws, ct).ConfigureAwait(false))
                        {
                            string source = Path.Combine(ws.RootDirectory, "source");
                            WriteFile(source, "top.txt", Content(50, 2000));
                            WriteFile(source, "docs/one.txt", Content(51, 2000));
                            WriteFile(source, "docs/two.txt", Content(52, 2000));

                            Policy policy = NewPolicy(source);
                            BackupEngine backup = new BackupEngine(fx.Database);
                            BackupJob job = await backup.RunAsync(policy, fx.Repository, fx.StorageTargetId, fx.EncryptionKey, fx.DataKey, fx.Chunking, BackupTypeEnum.Full, ct).ConfigureAwait(false);

                            string restore = Path.Combine(ws.RootDirectory, "restore");
                            RestoreJob rj = new RestoreJob();
                            rj.BackupJobId = job.Id;
                            rj.Scope = RestoreScopeEnum.Folder;
                            rj.SourceSelector = Path.Combine(source, "docs");
                            rj.DestinationRoot = restore;
                            RestoreEngine engine = new RestoreEngine(fx.Database);
                            RestoreJob done = await engine.RunAsync(rj, job, fx.Repository, fx.DataKey, ct).ConfigureAwait(false);

                            Check.Equal(2L, done.FilesRestored, "two files under the folder restored");
                            AssertRestored(source, restore, new[] { "docs/one.txt", "docs/two.txt" });
                            Check.False(File.Exists(MappedPath(restore, Path.Combine(source, "top.txt"))), "top file not restored");
                        }
                    }),

                    Case("VerifySucceedsThenFailsOnCorruption", "Verify passes, then fails on a corrupted chunk", async ct =>
                    {
                        using (TempWorkspace ws = new TempWorkspace())
                        using (EngineFixture fx = await EngineFixture.BuildAsync(ws, ct).ConfigureAwait(false))
                        {
                            string source = Path.Combine(ws.RootDirectory, "source");
                            WriteFile(source, "v.bin", Content(60, 15000));

                            Policy policy = NewPolicy(source);
                            BackupEngine backup = new BackupEngine(fx.Database);
                            BackupJob job = await backup.RunAsync(policy, fx.Repository, fx.StorageTargetId, fx.EncryptionKey, fx.DataKey, fx.Chunking, BackupTypeEnum.Full, ct).ConfigureAwait(false);

                            RestoreEngine engine = new RestoreEngine(fx.Database);
                            long verified = await engine.VerifyAsync(job, fx.Repository, fx.DataKey, ct).ConfigureAwait(false);
                            Check.True(verified > 0, "verify checked chunks");

                            CorruptOneChunk(Path.Combine(ws.RootDirectory, "repo"));
                            await Check.ThrowsAsync<ArmorException>(
                                () => engine.VerifyAsync(job, fx.Repository, fx.DataKey, ct),
                                "verify fails after corruption").ConfigureAwait(false);
                        }
                    }),

                    Case("RestoreCorruptChunkAborts", "Restore aborts on a corrupted chunk", async ct =>
                    {
                        using (TempWorkspace ws = new TempWorkspace())
                        using (EngineFixture fx = await EngineFixture.BuildAsync(ws, ct).ConfigureAwait(false))
                        {
                            string source = Path.Combine(ws.RootDirectory, "source");
                            WriteFile(source, "r.bin", Content(70, 15000));

                            Policy policy = NewPolicy(source);
                            BackupEngine backup = new BackupEngine(fx.Database);
                            BackupJob job = await backup.RunAsync(policy, fx.Repository, fx.StorageTargetId, fx.EncryptionKey, fx.DataKey, fx.Chunking, BackupTypeEnum.Full, ct).ConfigureAwait(false);

                            CorruptOneChunk(Path.Combine(ws.RootDirectory, "repo"));

                            string restore = Path.Combine(ws.RootDirectory, "restore");
                            RestoreJob rj = new RestoreJob();
                            rj.BackupJobId = job.Id;
                            rj.Scope = RestoreScopeEnum.All;
                            rj.DestinationRoot = restore;
                            RestoreEngine engine = new RestoreEngine(fx.Database);
                            await Check.ThrowsAsync<ArmorException>(
                                () => engine.RunAsync(rj, job, fx.Repository, fx.DataKey, ct),
                                "restore aborts on corruption").ConfigureAwait(false);
                        }
                    }),

                    Case("ExcludePatternsHonored", "Exclude patterns keep files out of the backup", async ct =>
                    {
                        using (TempWorkspace ws = new TempWorkspace())
                        using (EngineFixture fx = await EngineFixture.BuildAsync(ws, ct).ConfigureAwait(false))
                        {
                            string source = Path.Combine(ws.RootDirectory, "source");
                            WriteFile(source, "keep.txt", Content(80, 2000));
                            WriteFile(source, "skip.tmp", Content(81, 2000));
                            WriteFile(source, "cache/junk.txt", Content(82, 2000));

                            Policy policy = NewPolicy(source);
                            policy.ExcludePatterns.Add(new ExcludePattern("*.tmp", false, ExcludeTargetEnum.File));
                            policy.ExcludePatterns.Add(new ExcludePattern("cache", false, ExcludeTargetEnum.Directory));

                            BackupEngine backup = new BackupEngine(fx.Database);
                            BackupJob job = await backup.RunAsync(policy, fx.Repository, fx.StorageTargetId, fx.EncryptionKey, fx.DataKey, fx.Chunking, BackupTypeEnum.Full, ct).ConfigureAwait(false);
                            Check.Equal(1L, job.FileCount, "only the kept file was backed up");
                        }
                    }),

                    Case("BareNameExcludePrunesDirectory", "A bare-name exclude prunes a matching directory", async ct =>
                    {
                        using (TempWorkspace ws = new TempWorkspace())
                        using (EngineFixture fx = await EngineFixture.BuildAsync(ws, ct).ConfigureAwait(false))
                        {
                            string source = Path.Combine(ws.RootDirectory, "source");
                            WriteFile(source, "keep.txt", Content(70, 2000));
                            // A ".git" directory full of files the walk must never descend into, plus a file
                            // literally named ".git" elsewhere — a bare-name (Any) rule excludes both.
                            WriteFile(source, ".git/HEAD", Content(71, 2000));
                            WriteFile(source, ".git/objects/ab/cd", Content(72, 2000));
                            WriteFile(source, "nested/.git/config", Content(73, 2000));
                            WriteFile(source, "other/.git", Content(74, 2000));

                            Policy policy = NewPolicy(source);
                            policy.ExcludePatterns.Add(new ExcludePattern(".git", false, ExcludeTargetEnum.Any));

                            BackupEngine backup = new BackupEngine(fx.Database);
                            BackupJob job = await backup.RunAsync(policy, fx.Repository, fx.StorageTargetId, fx.EncryptionKey, fx.DataKey, fx.Chunking, BackupTypeEnum.Full, ct).ConfigureAwait(false);
                            Check.Equal(1L, job.FileCount, "only keep.txt survives; every .git file or folder is excluded");
                        }
                    }),

                    Case("SizeBoundsHonored", "Minimum and maximum size bounds filter files", async ct =>
                    {
                        using (TempWorkspace ws = new TempWorkspace())
                        using (EngineFixture fx = await EngineFixture.BuildAsync(ws, ct).ConfigureAwait(false))
                        {
                            string source = Path.Combine(ws.RootDirectory, "source");
                            WriteFile(source, "tiny.txt", Content(90, 100));
                            WriteFile(source, "ok.txt", Content(91, 5000));
                            WriteFile(source, "huge.bin", Content(92, 50000));

                            Policy policy = NewPolicy(source);
                            policy.MinFileSizeBytes = 1000;
                            policy.MaxFileSizeBytes = 10000;

                            BackupEngine backup = new BackupEngine(fx.Database);
                            BackupJob job = await backup.RunAsync(policy, fx.Repository, fx.StorageTargetId, fx.EncryptionKey, fx.DataKey, fx.Chunking, BackupTypeEnum.Full, ct).ConfigureAwait(false);
                            Check.Equal(1L, job.FileCount, "only the in-range file was backed up");
                        }
                    }),

                    Case("EmptyFileRoundTrip", "A zero-byte file backs up and restores", async ct =>
                    {
                        using (TempWorkspace ws = new TempWorkspace())
                        using (EngineFixture fx = await EngineFixture.BuildAsync(ws, ct).ConfigureAwait(false))
                        {
                            string source = Path.Combine(ws.RootDirectory, "source");
                            WriteFile(source, "empty.txt", Array.Empty<byte>());
                            WriteFile(source, "nonempty.txt", Content(100, 3000));

                            Policy policy = NewPolicy(source);
                            BackupEngine backup = new BackupEngine(fx.Database);
                            BackupJob job = await backup.RunAsync(policy, fx.Repository, fx.StorageTargetId, fx.EncryptionKey, fx.DataKey, fx.Chunking, BackupTypeEnum.Full, ct).ConfigureAwait(false);

                            string restore = Path.Combine(ws.RootDirectory, "restore");
                            await RestoreAllAsync(fx, job, restore, ct).ConfigureAwait(false);
                            AssertRestored(source, restore, new[] { "empty.txt", "nonempty.txt" });
                        }
                    }),

                    Case("ParallelBackupDeduplicatesAndRestores", "Parallel workers dedupe shared chunks and restore intact", async ct =>
                    {
                        using (TempWorkspace ws = new TempWorkspace())
                        using (EngineFixture fx = await EngineFixture.BuildAsync(ws, ct).ConfigureAwait(false))
                        {
                            // Many files across a handful of distinct contents, each content repeated so the
                            // same chunks are seen by several workers at once — this stresses the shared
                            // present-set and single-writer-per-hash coordination. Restoring every file
                            // byte-for-byte proves dedup, reference counting and per-file commits stayed
                            // correct under concurrency.
                            string source = Path.Combine(ws.RootDirectory, "source");
                            const int groups = 20;
                            const int copiesPerGroup = 8;
                            List<string> relatives = new List<string>();
                            for (int g = 0; g < groups; g++)
                            {
                                byte[] content = Content(1000 + g, 20000);
                                for (int c = 0; c < copiesPerGroup; c++)
                                {
                                    string relative = "g" + g + "/copy" + c + ".bin";
                                    WriteFile(source, relative, content);
                                    relatives.Add(relative);
                                }
                            }
                            int totalFiles = groups * copiesPerGroup;

                            Policy policy = NewPolicy(source);
                            BackupEngine backup = new BackupEngine(fx.Database);
                            BackupJob job = await backup.RunAsync(policy, fx.Repository, fx.StorageTargetId, fx.EncryptionKey, fx.DataKey, fx.Chunking, BackupTypeEnum.Full, ct, progress: null, maxParallelism: 8).ConfigureAwait(false);

                            Check.Equal((long)totalFiles, job.FileCount, "every file was backed up");
                            Check.True(job.ChunksReused > 0, "duplicate files reused chunks across workers");
                            Check.True(job.ChunksWritten > 0, "unique chunks were written");

                            string restore = Path.Combine(ws.RootDirectory, "restore");
                            await RestoreAllAsync(fx, job, restore, ct).ConfigureAwait(false);
                            AssertRestored(source, restore, relatives.ToArray());
                        }
                    })
                });
        }

        private static TestCaseDescriptor Case(string caseId, string displayName, Func<CancellationToken, Task> body)
        {
            return new TestCaseDescriptor(suiteId: "Engine", caseId: caseId, displayName: displayName, executeAsync: body);
        }

        private static Policy NewPolicy(string sourceDirectory)
        {
            Policy policy = new Policy();
            policy.Name = "test-policy";
            policy.IncludePaths.Add(sourceDirectory);
            policy.StorageTargetId = "tgt_test";
            return policy;
        }

        private static async Task RestoreAllAsync(EngineFixture fx, BackupJob job, string destinationRoot, CancellationToken ct)
        {
            RestoreJob rj = new RestoreJob();
            rj.BackupJobId = job.Id;
            rj.Scope = RestoreScopeEnum.All;
            rj.DestinationRoot = destinationRoot;
            RestoreEngine engine = new RestoreEngine(fx.Database);
            await engine.RunAsync(rj, job, fx.Repository, fx.DataKey, ct).ConfigureAwait(false);
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
                string restoredPath = MappedPath(restoreRoot, sourcePath);
                Check.True(File.Exists(restoredPath), "restored file exists: " + relative);
                byte[] expected = File.ReadAllBytes(sourcePath);
                byte[] actual = File.ReadAllBytes(restoredPath);
                Check.True(Equal(expected, actual), "restored file is byte-identical: " + relative);
            }
        }

        private static string MappedPath(string destinationRoot, string sourcePath)
        {
            string root = Path.GetPathRoot(sourcePath) ?? String.Empty;
            string relative = root.Length > 0 && sourcePath.StartsWith(root, StringComparison.Ordinal)
                ? sourcePath.Substring(root.Length)
                : sourcePath;
            relative = relative.TrimStart('/', '\\');
            return Path.Combine(destinationRoot, relative);
        }

        private static void CorruptOneChunk(string repoDirectory)
        {
            string chunksDirectory = Path.Combine(repoDirectory, "chunks");
            string[] files = Directory.GetFiles(chunksDirectory, "*", SearchOption.AllDirectories);
            if (files.Length == 0)
                throw new InvalidOperationException("No chunk files found to corrupt.");

            string target = files[0];
            byte[] bytes = File.ReadAllBytes(target);
            bytes[bytes.Length - 1] ^= 0xFF;
            File.WriteAllBytes(target, bytes);
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
