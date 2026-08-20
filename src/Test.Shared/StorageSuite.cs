namespace Test.Shared
{
    using System;
    using System.Collections.Generic;
    using System.Text;
    using System.Threading;
    using System.Threading.Tasks;
    using Armor.Core.ChunkStore;
    using Armor.Core.Enums;
    using Armor.Core.Exceptions;
    using Armor.Core.Models;
    using Armor.Core.Serialization;
    using Armor.Core.Storage;
    using Touchstone.Core;

    /// <summary>
    /// Verifies the storage repository against the configured provider (a local disk directory by
    /// default): connection validation, object and chunk round-trips, enumeration, repository-root
    /// isolation, and a full chunk backup-and-restore through frame/store/read/unframe.
    /// </summary>
    public static class StorageSuite
    {
        /// <summary>
        /// Build the storage test suite.
        /// </summary>
        /// <returns>The storage suite descriptor.</returns>
        public static TestSuiteDescriptor Build()
        {
            return new TestSuiteDescriptor(
                suiteId: "Storage",
                displayName: "Storage Repository",
                cases: new List<TestCaseDescriptor>
                {
                    Case("ValidateConnection", "Connection validation round-trips a probe", async ct =>
                    {
                        using (TempWorkspace ws = new TempWorkspace())
                        {
                            IStorageRepository repo = StorageRepositoryFactory.Create(TestStorage.Resolve(ws, NewPrefix()));
                            Check.True(await repo.ValidateConnectionAsync(ct).ConfigureAwait(false), "validation succeeds");
                        }
                    }),

                    Case("ObjectRoundTrip", "Object write/read/exists/delete", async ct =>
                    {
                        using (TempWorkspace ws = new TempWorkspace())
                        {
                            IStorageRepository repo = StorageRepositoryFactory.Create(TestStorage.Resolve(ws, NewPrefix()));
                            byte[] data = Encoding.UTF8.GetBytes("hello armor");
                            await repo.WriteObjectAsync("folder/obj.bin", data, ct).ConfigureAwait(false);
                            Check.True(await repo.ObjectExistsAsync("folder/obj.bin", ct).ConfigureAwait(false), "object exists after write");
                            byte[] read = await repo.ReadObjectAsync("folder/obj.bin", ct).ConfigureAwait(false);
                            Check.True(Equal(data, read), "object round-trips");
                            await repo.DeleteObjectAsync("folder/obj.bin", ct).ConfigureAwait(false);
                            Check.False(await repo.ObjectExistsAsync("folder/obj.bin", ct).ConfigureAwait(false), "object gone after delete");
                            await repo.DeleteObjectAsync("folder/obj.bin", ct).ConfigureAwait(false);
                        }
                    }),

                    Case("ChunkRoundTrip", "Chunk write/read/exists/delete", async ct =>
                    {
                        using (TempWorkspace ws = new TempWorkspace())
                        {
                            IStorageRepository repo = StorageRepositoryFactory.Create(TestStorage.Resolve(ws, NewPrefix()));
                            byte[] stored = Encoding.UTF8.GetBytes("framed-chunk-bytes");
                            string hash = Hasher.Sha256HexOfText("chunk-identity");
                            await repo.WriteChunkAsync(hash, stored, ct).ConfigureAwait(false);
                            Check.True(await repo.ChunkExistsAsync(hash, ct).ConfigureAwait(false), "chunk exists");
                            Check.True(Equal(stored, await repo.ReadChunkAsync(hash, ct).ConfigureAwait(false)), "chunk round-trips");
                            await repo.DeleteChunkAsync(hash, ct).ConfigureAwait(false);
                            Check.False(await repo.ChunkExistsAsync(hash, ct).ConfigureAwait(false), "chunk gone after delete");
                        }
                    }),

                    Case("EnumerateUnderPrefix", "Enumeration returns keys beneath a prefix", async ct =>
                    {
                        using (TempWorkspace ws = new TempWorkspace())
                        {
                            IStorageRepository repo = StorageRepositoryFactory.Create(TestStorage.Resolve(ws, NewPrefix()));
                            await repo.WriteObjectAsync("data/a", new byte[] { 1 }, ct).ConfigureAwait(false);
                            await repo.WriteObjectAsync("data/b", new byte[] { 2 }, ct).ConfigureAwait(false);
                            await repo.WriteObjectAsync("data/c", new byte[] { 3 }, ct).ConfigureAwait(false);
                            await repo.WriteObjectAsync("other/d", new byte[] { 4 }, ct).ConfigureAwait(false);

                            int count = 0;
                            await foreach (string key in repo.EnumerateKeysAsync("data/", ct).ConfigureAwait(false))
                                count++;
                            Check.Equal(3, count, "three keys under the data prefix");
                        }
                    }),

                    Case("RepositoryRootIsolation", "Repository roots isolate objects", async ct =>
                    {
                        using (TempWorkspace ws = new TempWorkspace())
                        {
                            string basePrefix = NewPrefix();
                            StorageTarget targetA = TestStorage.Resolve(ws, basePrefix + "/a");
                            StorageTarget targetB = TestStorage.Resolve(ws, basePrefix + "/b");
                            IStorageRepository repoA = StorageRepositoryFactory.Create(targetA);
                            IStorageRepository repoB = StorageRepositoryFactory.Create(targetB);

                            await repoA.WriteObjectAsync("shared/name", new byte[] { 9 }, ct).ConfigureAwait(false);
                            Check.True(await repoA.ObjectExistsAsync("shared/name", ct).ConfigureAwait(false), "object exists in repo A");
                            Check.False(await repoB.ObjectExistsAsync("shared/name", ct).ConfigureAwait(false), "object not visible in repo B");
                        }
                    }),

                    Case("ChunkBackupAndRestore", "A chunk frames, stores, reads back, and unframes", async ct =>
                    {
                        using (TempWorkspace ws = new TempWorkspace())
                        {
                            IStorageRepository repo = StorageRepositoryFactory.Create(TestStorage.Resolve(ws, NewPrefix()));
                            byte[] dataKey = new byte[32];
                            for (int i = 0; i < dataKey.Length; i++) dataKey[i] = (byte)(i * 3 + 1);

                            byte[] plaintext = Encoding.UTF8.GetBytes(new string('Z', 10000));
                            string hash = Hasher.Sha256Hex(plaintext);

                            byte[] stored = ChunkFramer.Frame(plaintext, dataKey, hash);
                            await repo.WriteChunkAsync(hash, stored, ct).ConfigureAwait(false);

                            byte[] fetched = await repo.ReadChunkAsync(hash, ct).ConfigureAwait(false);
                            byte[] restored = ChunkFramer.Unframe(fetched, dataKey, hash);
                            Check.True(Equal(plaintext, restored), "chunk restores byte-identically from storage");
                        }
                    }),

                    Case("ManifestRoundTrip", "A manifest serializes, stores, and restores", async ct =>
                    {
                        using (TempWorkspace ws = new TempWorkspace())
                        {
                            IStorageRepository repo = StorageRepositoryFactory.Create(TestStorage.Resolve(ws, NewPrefix()));
                            Manifest manifest = new Manifest();
                            manifest.JobId = "job_test";
                            manifest.PolicyId = "pol_test";
                            manifest.BackupType = BackupTypeEnum.Full;
                            ManifestFileEntry entry = new ManifestFileEntry();
                            entry.Path = "/data/file.txt";
                            entry.SizeBytes = 3;
                            entry.ChunkHashes.Add("aaaa");
                            entry.ChunkHashes.Add("bbbb");
                            manifest.Files.Add(entry);

                            string key = RepositoryKeys.ManifestKey(manifest.PolicyId, manifest.JobId);
                            byte[] bytes = Encoding.UTF8.GetBytes(ArmorJson.Serialize(manifest));
                            await repo.WriteObjectAsync(key, bytes, ct).ConfigureAwait(false);

                            byte[] readBytes = await repo.ReadObjectAsync(key, ct).ConfigureAwait(false);
                            Manifest? restored = ArmorJson.Deserialize<Manifest>(Encoding.UTF8.GetString(readBytes));
                            Check.NotNull(restored, "manifest deserializes");
                            Check.Equal("job_test", restored!.JobId, "job id round-trips");
                            Check.Equal(1, restored.Files.Count, "file entry round-trips");
                            Check.Equal(2, restored.Files[0].ChunkHashes.Count, "chunk hashes round-trip");
                        }
                    }),

                    Case("ReadMissingObjectThrows", "Reading a missing object throws a storage exception", async ct =>
                    {
                        using (TempWorkspace ws = new TempWorkspace())
                        {
                            IStorageRepository repo = StorageRepositoryFactory.Create(TestStorage.Resolve(ws, NewPrefix()));
                            await Check.ThrowsAsync<ArmorStorageException>(
                                () => repo.ReadObjectAsync("nope/missing", ct),
                                "missing read throws").ConfigureAwait(false);
                        }
                    })
                });
        }

        private static TestCaseDescriptor Case(string caseId, string displayName, Func<CancellationToken, Task> body)
        {
            return new TestCaseDescriptor(suiteId: "Storage", caseId: caseId, displayName: displayName, executeAsync: body);
        }

        private static string NewPrefix()
        {
            return "t" + Guid.NewGuid().ToString("N");
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
