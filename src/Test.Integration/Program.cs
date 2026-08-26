namespace Test.Integration
{
    using System;
    using System.Collections.Generic;
    using System.IO;
    using System.Threading;
    using System.Threading.Tasks;
    using Armor.Core.Configuration;
    using Armor.Core.Enums;
    using Armor.Core.Exceptions;
    using Armor.Core.Models;
    using Armor.Core.Security;
    using Armor.Core.Service;

    /// <summary>
    /// A CLI-driven integration harness that exercises backup, catalog enumeration, and restore against
    /// a real backup target — a temporary disk directory or an object-storage bucket — with the target
    /// configured entirely from command-line arguments. It runs positive and negative cases and, unless
    /// told otherwise, cleans up everything it created afterward.
    /// </summary>
    public static class Program
    {
        private static int _Failures;
        private static int _Passed;

        /// <summary>
        /// Program entry point.
        /// </summary>
        /// <param name="args">Command-line arguments; see <see cref="PrintUsage"/>.</param>
        /// <returns>Zero when every case passed; otherwise the number of failures.</returns>
        public static async Task<int> Main(string[] args)
        {
            Args a = Args.Parse(args);
            if (a.Flag("help") || a.Flag("h"))
            {
                PrintUsage();
                return 0;
            }

            string type = a.Value("type", "disk").ToLowerInvariant();
            bool cleanup = !a.Flag("no-cleanup") && !a.Flag("keep");
            string encPassword = a.Value("enc-password", "integration-test-password");

            string workRoot = a.Value("work-dir", Path.Combine(Path.GetTempPath(), "armor-itest-" + DateTime.UtcNow.ToString("yyyyMMddHHmmss")));
            string home = Path.Combine(workRoot, "home");
            string source = Path.Combine(workRoot, "source");
            string restoreAll = Path.Combine(workRoot, "restore-all");
            string restoreLatest = Path.Combine(workRoot, "restore-latest");
            string restoreOne = Path.Combine(workRoot, "restore-one");
            Directory.CreateDirectory(workRoot);

            Console.WriteLine("Armor integration test");
            Console.WriteLine("  target type : " + type);
            Console.WriteLine("  work dir    : " + workRoot);
            Console.WriteLine("  cleanup     : " + (cleanup ? "yes" : "no (--no-cleanup)"));
            Console.WriteLine();

            ArmorContext? context = null;
            StorageTarget? target = null;
            StorageTargetService? targetService = null;

            try
            {
                ArmorPaths paths = new ArmorPaths(home);
                paths.EnsureDirectories();
                context = await ArmorContext.CreateAsync(paths).ConfigureAwait(false);

                // Small chunks so the files split into several chunks and dedup/incremental is exercised.
                context.Settings.Chunking.MinSizeBytes = 1024;
                context.Settings.Chunking.AvgSizeBytes = 2048;
                context.Settings.Chunking.MaxSizeBytes = 8192;

                target = BuildTarget(type, a);
                targetService = new StorageTargetService(context.Database, context.CredentialProtector);
                await targetService.CreateAsync(target).ConfigureAwait(false);

                EncryptionKeyService keyService = new EncryptionKeyService(context.Database);
                ProvisionedKey provisioned = await keyService.ProvisionAsync("itest", encPassword, null, 50000).ConfigureAwait(false);

                // ---- source data ----
                WriteFile(source, "docs/readme.txt", Content(1, 6000));
                WriteFile(source, "docs/notes.txt", Content(2, 3000));
                WriteFile(source, "data/blob.bin", Content(3, 24000));

                Policy policy = new Policy();
                policy.Name = "itest-policy";
                policy.IncludePaths.Add(source);
                policy.StorageTargetId = target.Id;
                policy.EncryptionKeyId = provisioned.Key.Id;
                await context.Database.Policies.CreateAsync(policy).ConfigureAwait(false);

                BackupService backupService = new BackupService(context);
                RestoreService restoreService = new RestoreService(context);
                StorageTarget targetLocal = target;
                ArmorContext ctx = context;

                await Run("connection validates (probe write/read/delete)", async () =>
                {
                    bool ok = await targetService.ValidateAsync(targetLocal.Id).ConfigureAwait(false);
                    Check(ok, "ValidateAsync returned false");
                });

                BackupJob job1 = new BackupJob();
                await Run("full backup completes", async () =>
                {
                    job1 = await backupService.RunAsync(policy.Id, provisioned.DataKey, BackupTypeEnum.Full, true).ConfigureAwait(false);
                    Check(job1.Status == JobStatusEnum.Completed, "backup status was " + job1.Status);
                    Check(job1.FileCount == 3, "expected 3 files, got " + job1.FileCount);
                });

                await Run("verify authenticates every chunk", async () =>
                {
                    long verified = await restoreService.VerifyAsync(job1.Id, provisioned.DataKey).ConfigureAwait(false);
                    Check(verified > 0, "verify checked no chunks");
                });

                await Run("enumerate the catalog from the target (password only)", async () =>
                {
                    RecoverySession session = await new RecoveryService(ctx).OpenAsync(targetLocal.Id, encPassword).ConfigureAwait(false);
                    List<RecoveryPoint> points = await session.BrowseAsync().ConfigureAwait(false);
                    Check(points.Count == 1, "expected 1 recovery point, got " + points.Count);
                    Check((int)points[0].FileCount == 3, "recovery point should list 3 files");
                });

                await Run("restore everything, byte-identical", async () =>
                {
                    RestoreJob rj = new RestoreJob { BackupJobId = job1.Id, Scope = RestoreScopeEnum.All, DestinationRoot = restoreAll };
                    RestoreJob done = await restoreService.RunAsync(rj, provisioned.DataKey).ConfigureAwait(false);
                    Check(done.Status == JobStatusEnum.Completed, "restore status was " + done.Status);
                    AssertRestored(source, restoreAll, new[] { "docs/readme.txt", "docs/notes.txt", "data/blob.bin" });
                });

                // ---- change the source, run an incremental ----
                WriteFile(source, "docs/notes.txt", Content(42, 4000));   // modified
                WriteFile(source, "data/added.bin", Content(7, 12000));    // new
                BackupJob job2 = new BackupJob();
                await Run("incremental backup after changes completes", async () =>
                {
                    job2 = await backupService.RunAsync(policy.Id, provisioned.DataKey, BackupTypeEnum.Incremental, true).ConfigureAwait(false);
                    Check(job2.Status == JobStatusEnum.Completed, "incremental status was " + job2.Status);
                    Check(job2.FileCount == 4, "expected 4 files, got " + job2.FileCount);
                });

                await Run("catalog now lists two points", async () =>
                {
                    RecoverySession session = await new RecoveryService(ctx).OpenAsync(targetLocal.Id, encPassword).ConfigureAwait(false);
                    List<RecoveryPoint> points = await session.BrowseAsync().ConfigureAwait(false);
                    Check(points.Count == 2, "expected 2 recovery points, got " + points.Count);
                });

                await Run("restore latest reflects the changes", async () =>
                {
                    RestoreJob rj = new RestoreJob { BackupJobId = job2.Id, Scope = RestoreScopeEnum.All, DestinationRoot = restoreLatest };
                    await restoreService.RunAsync(rj, provisioned.DataKey).ConfigureAwait(false);
                    AssertRestored(source, restoreLatest, new[] { "docs/readme.txt", "docs/notes.txt", "data/blob.bin", "data/added.bin" });
                });

                await Run("partial restore of a single file", async () =>
                {
                    string one = Path.Combine(source, "docs".Replace('/', Path.DirectorySeparatorChar), "readme.txt");
                    RestoreJob rj = new RestoreJob { BackupJobId = job2.Id, Scope = RestoreScopeEnum.File, SourceSelector = one, DestinationRoot = restoreOne };
                    RestoreJob done = await restoreService.RunAsync(rj, provisioned.DataKey).ConfigureAwait(false);
                    Check(done.FilesRestored == 1, "expected 1 file restored, got " + done.FilesRestored);
                    AssertRestored(source, restoreOne, new[] { "docs/readme.txt" });
                });

                // ---- negative cases ----
                await Run("NEGATIVE: wrong password is rejected", async () =>
                {
                    bool threw = false;
                    try { await new RecoveryService(ctx).OpenAsync(targetLocal.Id, "not-the-password").ConfigureAwait(false); }
                    catch (ArmorCryptoException) { threw = true; }
                    Check(threw, "wrong password did not throw ArmorCryptoException");
                });

                await Run("NEGATIVE: restoring a non-existent point fails", async () =>
                {
                    bool threw = false;
                    try
                    {
                        RestoreJob rj = new RestoreJob { BackupJobId = "job_does_not_exist", Scope = RestoreScopeEnum.All, DestinationRoot = restoreOne };
                        await restoreService.RunAsync(rj, provisioned.DataKey).ConfigureAwait(false);
                    }
                    catch (ArmorException) { threw = true; }
                    Check(threw, "restoring a bogus job did not throw");
                });

                await Run("NEGATIVE: opening a location with no repository fails", async () =>
                {
                    // A fresh empty target (nothing backed up) has no repository header.
                    StorageTarget empty = BuildTarget(type, a);
                    empty.Name = "itest-empty";
                    if (empty.Type == StorageTargetTypeEnum.Disk)
                        empty.DiskPath = Path.Combine(workRoot, "empty-repo");
                    else
                        empty.RepositoryRoot = "armor-itest-empty-" + DateTime.UtcNow.ToString("HHmmssfff");
                    await targetService.CreateAsync(empty).ConfigureAwait(false);

                    bool threw = false;
                    try { await new RecoveryService(ctx).OpenAsync(empty.Id, encPassword).ConfigureAwait(false); }
                    catch (ArmorException) { threw = true; }
                    Check(threw, "opening an empty location did not throw");

                    if (cleanup)
                    {
                        try { await targetService.PurgeAsync(empty.Id).ConfigureAwait(false); } catch { }
                    }
                });
            }
            catch (Exception ex)
            {
                _Failures++;
                Console.WriteLine("FATAL  " + ex.GetType().Name + ": " + ex.Message);
            }
            finally
            {
                if (cleanup)
                {
                    Console.WriteLine();
                    Console.WriteLine("Cleaning up");
                    if (targetService != null && target != null)
                    {
                        try
                        {
                            int deleted = await targetService.PurgeAsync(target.Id).ConfigureAwait(false);
                            Console.WriteLine("  purged " + deleted + " objects from the target");
                        }
                        catch (Exception ex)
                        {
                            Console.WriteLine("  purge failed: " + ex.Message);
                        }
                    }
                    context?.Dispose();
                    // Release the pooled SQLite handle so the database file can be deleted.
                    Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
                    TryDeleteDirectory(workRoot);
                }
                else
                {
                    context?.Dispose();
                    Console.WriteLine();
                    Console.WriteLine("Left in place (--no-cleanup): " + workRoot);
                    if (target != null)
                        Console.WriteLine("Target data NOT purged; remove it yourself if needed.");
                }
            }

            Console.WriteLine();
            Console.WriteLine("OVERALL " + (_Failures == 0 ? "PASS" : "FAIL") + "   passed=" + _Passed + " failed=" + _Failures);
            return _Failures;
        }

        private static StorageTarget BuildTarget(string type, Args a)
        {
            StorageTarget target = new StorageTarget();
            target.Name = "itest-target";

            switch (type)
            {
                case "disk":
                    target.Type = StorageTargetTypeEnum.Disk;
                    target.DiskPath = a.Value("path", Path.Combine(a.Value("work-dir", Path.GetTempPath()), "itest-repo-" + Guid.NewGuid().ToString("N").Substring(0, 8)));
                    break;

                case "s3":
                case "amazons3":
                    target.Type = StorageTargetTypeEnum.AmazonS3;
                    target.AccessKey = Require(a, "access-key");
                    target.SecretKey = Require(a, "secret-key");
                    target.Region = a.Value("region", "us-east-1");
                    target.Bucket = Require(a, "bucket");
                    string? endpoint = a.ValueOrNull("endpoint");
                    if (!string.IsNullOrWhiteSpace(endpoint))
                        target.Endpoint = endpoint;
                    target.UseSsl = a.Flag("use-ssl") || (!a.Flag("no-ssl") && (endpoint == null || endpoint.StartsWith("https", StringComparison.OrdinalIgnoreCase)));
                    string? baseUrl = a.ValueOrNull("base-url");
                    if (!string.IsNullOrWhiteSpace(baseUrl))
                        target.BaseUrl = baseUrl;
                    else if (!string.IsNullOrWhiteSpace(endpoint) && (a.Flag("path-style") || a.Flag("virtual-hosted")))
                        target.BaseUrl = BuildS3BaseUrl(endpoint!, a.Flag("path-style"));
                    break;

                case "azure":
                case "azureblob":
                    target.Type = StorageTargetTypeEnum.AzureBlob;
                    target.AccountName = Require(a, "account-name");
                    target.AccountKey = Require(a, "account-key");
                    target.Endpoint = Require(a, "endpoint");
                    target.Container = Require(a, "container");
                    break;

                case "gcs":
                case "google":
                case "googlecloud":
                    target.Type = StorageTargetTypeEnum.GoogleCloud;
                    target.ProjectId = Require(a, "project-id");
                    target.Bucket = Require(a, "bucket");
                    string credPath = Require(a, "credential-json");
                    target.CredentialJson = File.ReadAllText(credPath);
                    break;

                case "cifs":
                case "smb":
                    target.Type = StorageTargetTypeEnum.Cifs;
                    target.Host = Require(a, "host");
                    target.ShareName = Require(a, "share");
                    target.Username = Require(a, "username");
                    target.Password = Require(a, "password");
                    break;

                case "nfs":
                    target.Type = StorageTargetTypeEnum.Nfs;
                    target.Host = Require(a, "host");
                    target.ShareName = Require(a, "share");
                    target.NfsVersion = a.Value("nfs-version", "V3");
                    break;

                default:
                    throw new ArgumentException("Unknown --type '" + type + "'. Use disk, s3, azure, gcs, cifs, or nfs.");
            }

            string? repoRoot = a.ValueOrNull("repo-root");
            if (!string.IsNullOrWhiteSpace(repoRoot))
                target.RepositoryRoot = repoRoot;
            return target;
        }

        private static string BuildS3BaseUrl(string endpoint, bool pathStyle)
        {
            Uri uri = new Uri(endpoint);
            string scheme = uri.Scheme;
            string host = uri.IsDefaultPort ? uri.Host : uri.Host + ":" + uri.Port;
            // Blobject template placeholders: {bucket} and {key}.
            return pathStyle
                ? scheme + "://" + host + "/{bucket}/{key}"
                : scheme + "://{bucket}." + host + "/{key}";
        }

        private static string Require(Args a, string key)
        {
            string? v = a.ValueOrNull(key);
            if (string.IsNullOrWhiteSpace(v))
                throw new ArgumentException("Missing required argument --" + key + " for this target type.");
            return v!;
        }

        private static async Task Run(string name, Func<Task> body)
        {
            try
            {
                await body().ConfigureAwait(false);
                _Passed++;
                Console.WriteLine("PASS  " + name);
            }
            catch (Exception ex)
            {
                _Failures++;
                Console.WriteLine("FAIL  " + name + "  ->  " + ex.GetType().Name + ": " + ex.Message);
            }
        }

        private static void Check(bool condition, string message)
        {
            if (!condition)
                throw new Exception(message);
        }

        private static void WriteFile(string root, string relative, byte[] content)
        {
            string full = Path.Combine(root, relative.Replace('/', Path.DirectorySeparatorChar));
            string? directory = Path.GetDirectoryName(full);
            if (!string.IsNullOrEmpty(directory))
                Directory.CreateDirectory(directory);
            File.WriteAllBytes(full, content);
        }

        private static void AssertRestored(string sourceRoot, string restoreRoot, string[] relativePaths)
        {
            foreach (string relative in relativePaths)
            {
                string sourcePath = Path.Combine(sourceRoot, relative.Replace('/', Path.DirectorySeparatorChar));
                string pathRoot = Path.GetPathRoot(sourcePath) ?? string.Empty;
                string rel = pathRoot.Length > 0 && sourcePath.StartsWith(pathRoot, StringComparison.Ordinal) ? sourcePath.Substring(pathRoot.Length) : sourcePath;
                rel = rel.TrimStart('/', '\\');
                string restoredPath = Path.Combine(restoreRoot, rel);
                Check(File.Exists(restoredPath), "restored file missing: " + relative);
                Check(BytesEqual(File.ReadAllBytes(sourcePath), File.ReadAllBytes(restoredPath)), "restored bytes differ: " + relative);
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

        private static bool BytesEqual(byte[] a, byte[] b)
        {
            if (a.Length != b.Length)
                return false;
            for (int i = 0; i < a.Length; i++)
                if (a[i] != b[i])
                    return false;
            return true;
        }

        private static void TryDeleteDirectory(string path)
        {
            // The SQLite database file can linger in a connection pool briefly after the context is
            // disposed; finalize and retry so the temp directory actually gets removed.
            for (int attempt = 0; attempt < 4; attempt++)
            {
                try
                {
                    if (Directory.Exists(path))
                        Directory.Delete(path, true);
                    Console.WriteLine("  removed " + path);
                    return;
                }
                catch (IOException)
                {
                    GC.Collect();
                    GC.WaitForPendingFinalizers();
                    Thread.Sleep(200);
                }
                catch (Exception ex)
                {
                    Console.WriteLine("  could not remove " + path + ": " + ex.Message);
                    return;
                }
            }
            Console.WriteLine("  could not remove " + path + " (still locked); remove it manually.");
        }

        private static void PrintUsage()
        {
            Console.WriteLine("Armor integration test — backup, enumerate, and restore against a real target.");
            Console.WriteLine();
            Console.WriteLine("Common:");
            Console.WriteLine("  --type <disk|s3|azure|gcs|cifs|nfs>   Target type (default: disk)");
            Console.WriteLine("  --work-dir <dir>                      Where temp home/source/restore live (default: system temp)");
            Console.WriteLine("  --enc-password <pw>                   Backup password (default: a fixed test value)");
            Console.WriteLine("  --repo-root <prefix>                  Repository root prefix on the target (optional)");
            Console.WriteLine("  --no-cleanup | --keep                 Leave temp files and target data in place (default: clean up)");
            Console.WriteLine();
            Console.WriteLine("disk:  --path <dir>");
            Console.WriteLine("s3:    --access-key --secret-key --bucket [--region us-east-1]");
            Console.WriteLine("       [--endpoint http://host:9000] [--base-url tmpl] [--path-style | --virtual-hosted] [--use-ssl | --no-ssl]");
            Console.WriteLine("azure: --account-name --account-key --endpoint --container");
            Console.WriteLine("gcs:   --project-id --bucket --credential-json <path>");
            Console.WriteLine("cifs:  --host --share --username --password");
            Console.WriteLine("nfs:   --host --share [--nfs-version V3]");
            Console.WriteLine();
            Console.WriteLine("Example (local MinIO, path-style):");
            Console.WriteLine("  dotnet run --project src/Test.Integration -- --type s3 \\");
            Console.WriteLine("    --endpoint http://localhost:9000 --access-key A --secret-key B \\");
            Console.WriteLine("    --bucket armor-test --region us-east-1 --path-style --no-cleanup");
        }

        /// <summary>
        /// A tiny <c>--key value</c> / <c>--flag</c> argument parser.
        /// </summary>
        private sealed class Args
        {
            private readonly Dictionary<string, string> _Values = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            private readonly HashSet<string> _Flags = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            public static Args Parse(string[] args)
            {
                Args result = new Args();
                for (int i = 0; i < args.Length; i++)
                {
                    string arg = args[i];
                    if (!arg.StartsWith("--", StringComparison.Ordinal))
                        continue;
                    string key = arg.Substring(2);
                    if (i + 1 < args.Length && !args[i + 1].StartsWith("--", StringComparison.Ordinal))
                    {
                        result._Values[key] = args[i + 1];
                        i++;
                    }
                    else
                    {
                        result._Flags.Add(key);
                    }
                }
                return result;
            }

            public string Value(string key, string fallback)
            {
                return _Values.TryGetValue(key, out string? v) ? v : fallback;
            }

            public string? ValueOrNull(string key)
            {
                return _Values.TryGetValue(key, out string? v) ? v : null;
            }

            public bool Flag(string key)
            {
                return _Flags.Contains(key);
            }
        }
    }
}
