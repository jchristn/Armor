namespace Armor.Core.Engine
{
    using System;
    using System.Collections.Generic;
    using System.IO;
    using System.Runtime.CompilerServices;
    using System.Threading;
    using System.Threading.Tasks;
    using Armor.Core.Models;

    /// <summary>
    /// Walks a policy's include paths and yields the files that pass its exclude patterns and size limits.
    /// Directory listings are read through <see cref="DirectoryInfo"/> so each file's size, timestamp, and
    /// attributes come straight from the directory enumeration — no extra per-file stat — and the tree is
    /// walked once. Directory reads that fail (for example permission denied) are skipped rather than
    /// throwing.
    /// </summary>
    public sealed class FileEnumerator
    {
        /// <summary>
        /// Enumerate the files a policy includes, with the metadata read from the directory walk.
        /// </summary>
        /// <param name="policy">The policy. Cannot be null.</param>
        /// <returns>The included files.</returns>
        /// <exception cref="ArgumentNullException">Thrown when <paramref name="policy"/> is null.</exception>
        public IEnumerable<ScannedFile> Scan(Policy policy)
        {
            if (policy == null)
                throw new ArgumentNullException(nameof(policy));

            ExcludeMatcher matcher = new ExcludeMatcher(policy.ExcludePatterns);

            foreach (string includePath in policy.IncludePaths)
            {
                if (File.Exists(includePath))
                {
                    ScannedFile? single = TryScan(new FileInfo(includePath), policy, matcher);
                    if (single != null)
                        yield return single;
                }
                else if (Directory.Exists(includePath))
                {
                    foreach (ScannedFile file in Walk(includePath, policy, matcher))
                        yield return file;
                }
            }
        }

        /// <summary>
        /// Enumerate the files a policy includes, asynchronously. The walk itself is synchronous; the
        /// method yields to the scheduler periodically so cancellation stays responsive on very large trees.
        /// </summary>
        /// <param name="policy">The policy. Cannot be null.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>An async sequence of included files.</returns>
        /// <exception cref="ArgumentNullException">Thrown when <paramref name="policy"/> is null.</exception>
        public async IAsyncEnumerable<ScannedFile> ScanAsync(Policy policy, [EnumeratorCancellation] CancellationToken token = default)
        {
            if (policy == null)
                throw new ArgumentNullException(nameof(policy));

            int counter = 0;
            foreach (ScannedFile file in Scan(policy))
            {
                token.ThrowIfCancellationRequested();
                yield return file;
                if ((++counter & 1023) == 0)
                    await Task.Yield();
            }
        }

        private static IEnumerable<ScannedFile> Walk(string root, Policy policy, ExcludeMatcher matcher)
        {
            Stack<string> pending = new Stack<string>();
            pending.Push(root);

            while (pending.Count > 0)
            {
                string current = pending.Pop();
                DirectoryInfo directory = new DirectoryInfo(current);

                FileInfo[] files;
                try
                {
                    files = directory.GetFiles();
                }
                catch (UnauthorizedAccessException)
                {
                    continue;
                }
                catch (DirectoryNotFoundException)
                {
                    continue;
                }
                catch (IOException)
                {
                    continue;
                }

                foreach (FileInfo file in files)
                {
                    ScannedFile? scanned = TryScan(file, policy, matcher);
                    if (scanned != null)
                        yield return scanned;
                }

                DirectoryInfo[] subdirectories;
                try
                {
                    subdirectories = directory.GetDirectories();
                }
                catch (UnauthorizedAccessException)
                {
                    continue;
                }
                catch (DirectoryNotFoundException)
                {
                    continue;
                }
                catch (IOException)
                {
                    continue;
                }

                foreach (DirectoryInfo subdirectory in subdirectories)
                {
                    if (!matcher.IsDirectoryExcluded(subdirectory.FullName))
                        pending.Push(subdirectory.FullName);
                }
            }
        }

        private static ScannedFile? TryScan(FileInfo file, Policy policy, ExcludeMatcher matcher)
        {
            if (matcher.IsFileExcluded(file.FullName))
                return null;

            long length;
            try
            {
                length = file.Length;
            }
            catch (FileNotFoundException)
            {
                return null;
            }
            catch (IOException)
            {
                return null;
            }

            if (policy.MinFileSizeBytes > 0 && length < policy.MinFileSizeBytes)
                return null;
            if (policy.MaxFileSizeBytes > 0 && length > policy.MaxFileSizeBytes)
                return null;

            ScannedFile scanned = new ScannedFile();
            scanned.Path = file.FullName;
            scanned.SizeBytes = length;
            try
            {
                scanned.ModifiedUtc = file.LastWriteTimeUtc;
            }
            catch (IOException)
            {
                scanned.ModifiedUtc = DateTime.MinValue;
            }
            try
            {
                scanned.ArchiveBit = (file.Attributes & FileAttributes.Archive) == FileAttributes.Archive;
            }
            catch (IOException)
            {
                scanned.ArchiveBit = false;
            }
            return scanned;
        }
    }
}
