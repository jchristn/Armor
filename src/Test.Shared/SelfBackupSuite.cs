namespace Test.Shared
{
    using System;
    using System.Collections.Generic;
    using System.IO;
    using System.Threading;
    using System.Threading.Tasks;
    using Armor.Core.Backup;
    using Touchstone.Core;

    /// <summary>
    /// Verifies the self-backup facility: the configuration file, database, and state directory export
    /// to a single zip and import back byte-identically.
    /// </summary>
    public static class SelfBackupSuite
    {
        /// <summary>
        /// Build the self-backup test suite.
        /// </summary>
        /// <returns>The self-backup suite descriptor.</returns>
        public static TestSuiteDescriptor Build()
        {
            return new TestSuiteDescriptor(
                suiteId: "SelfBackup",
                displayName: "Self-Backup",
                cases: new List<TestCaseDescriptor>
                {
                    Case("ExportImportRoundTrip", "Config, database, and state round-trip through a zip", async ct =>
                    {
                        using (TempWorkspace ws = new TempWorkspace())
                        {
                            string sourceHome = Path.Combine(ws.RootDirectory, "home");
                            Directory.CreateDirectory(sourceHome);
                            string configFile = Path.Combine(sourceHome, "armor.json");
                            string databaseFile = Path.Combine(sourceHome, "armor.db");
                            string stateDirectory = Path.Combine(sourceHome, "state");
                            Directory.CreateDirectory(stateDirectory);

                            byte[] configBytes = System.Text.Encoding.UTF8.GetBytes("{\"CreatedUtc\":\"2026-01-01T00:00:00Z\"}");
                            byte[] databaseBytes = Content(1, 4096);
                            byte[] stateBytes = Content(2, 2048);
                            File.WriteAllBytes(configFile, configBytes);
                            File.WriteAllBytes(databaseFile, databaseBytes);
                            string nestedStateFile = Path.Combine(stateDirectory, "nested/lock.dat".Replace('/', Path.DirectorySeparatorChar));
                            Directory.CreateDirectory(Path.GetDirectoryName(nestedStateFile)!);
                            File.WriteAllBytes(nestedStateFile, stateBytes);

                            string zipPath = Path.Combine(ws.RootDirectory, "armor-backup.zip");
                            await ConfigBackup.ExportAsync(configFile, databaseFile, stateDirectory, zipPath, ct).ConfigureAwait(false);
                            Check.True(File.Exists(zipPath), "backup zip created");

                            string targetHome = Path.Combine(ws.RootDirectory, "restored");
                            string targetConfig = Path.Combine(targetHome, "armor.json");
                            string targetDatabase = Path.Combine(targetHome, "armor.db");
                            string targetState = Path.Combine(targetHome, "state");

                            await ConfigBackup.ImportAsync(zipPath, targetConfig, targetDatabase, targetState, ct).ConfigureAwait(false);

                            Check.True(Equal(configBytes, File.ReadAllBytes(targetConfig)), "config round-trips");
                            Check.True(Equal(databaseBytes, File.ReadAllBytes(targetDatabase)), "database round-trips");
                            string restoredState = Path.Combine(targetState, "nested/lock.dat".Replace('/', Path.DirectorySeparatorChar));
                            Check.True(File.Exists(restoredState), "nested state file restored");
                            Check.True(Equal(stateBytes, File.ReadAllBytes(restoredState)), "state round-trips");
                        }
                    }),

                    Case("ImportMissingZipThrows", "Importing a missing archive throws", async ct =>
                    {
                        using (TempWorkspace ws = new TempWorkspace())
                        {
                            await Check.ThrowsAsync<FileNotFoundException>(
                                () => ConfigBackup.ImportAsync(
                                    Path.Combine(ws.RootDirectory, "nope.zip"),
                                    Path.Combine(ws.RootDirectory, "armor.json"),
                                    Path.Combine(ws.RootDirectory, "armor.db"),
                                    Path.Combine(ws.RootDirectory, "state"),
                                    ct),
                                "missing archive throws").ConfigureAwait(false);
                        }
                    })
                });
        }

        private static TestCaseDescriptor Case(string caseId, string displayName, Func<CancellationToken, Task> body)
        {
            return new TestCaseDescriptor(suiteId: "SelfBackup", caseId: caseId, displayName: displayName, executeAsync: body);
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
