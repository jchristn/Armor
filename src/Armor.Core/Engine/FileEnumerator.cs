namespace Armor.Core.Engine
{
    using System;
    using System.Collections.Generic;
    using System.IO;
    using System.Runtime.CompilerServices;
    using System.Threading;
    using Armor.Core.Models;

    /// <summary>
    /// Enumerates the files a policy includes, applying its exclude patterns (pruning excluded
    /// directories) and minimum and maximum size bounds. Directories that cannot be read are skipped
    /// rather than failing the whole run. This type is stateless and thread-safe.
    /// </summary>
    public sealed class FileEnumerator
    {
        /// <summary>
        /// Enumerate the absolute paths of files included by a policy.
        /// </summary>
        /// <param name="policy">The policy. Cannot be null.</param>
        /// <returns>The included file paths.</returns>
        /// <exception cref="ArgumentNullException">Thrown when <paramref name="policy"/> is null.</exception>
        public IEnumerable<string> Enumerate(Policy policy)
        {
            if (policy == null)
                throw new ArgumentNullException(nameof(policy));

            ExcludeMatcher matcher = new ExcludeMatcher(policy.ExcludePatterns);

            foreach (string includePath in policy.IncludePaths)
            {
                if (File.Exists(includePath))
                {
                    if (Passes(includePath, policy, matcher))
                        yield return includePath;
                }
                else if (Directory.Exists(includePath))
                {
                    foreach (string file in Walk(includePath, policy, matcher))
                        yield return file;
                }
            }
        }

        /// <summary>
        /// Enumerate the absolute paths of files included by a policy, asynchronously.
        /// </summary>
        /// <param name="policy">The policy. Cannot be null.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>An async sequence of included file paths.</returns>
        /// <exception cref="ArgumentNullException">Thrown when <paramref name="policy"/> is null.</exception>
        public async IAsyncEnumerable<string> EnumerateAsync(Policy policy, [EnumeratorCancellation] CancellationToken token = default)
        {
            if (policy == null)
                throw new ArgumentNullException(nameof(policy));

            foreach (string file in Enumerate(policy))
            {
                token.ThrowIfCancellationRequested();
                yield return file;
                await System.Threading.Tasks.Task.Yield();
            }
        }

        private static IEnumerable<string> Walk(string root, Policy policy, ExcludeMatcher matcher)
        {
            Stack<string> pending = new Stack<string>();
            pending.Push(root);

            while (pending.Count > 0)
            {
                string current = pending.Pop();

                string[] files;
                try
                {
                    files = Directory.GetFiles(current);
                }
                catch (UnauthorizedAccessException)
                {
                    continue;
                }
                catch (IOException)
                {
                    continue;
                }

                foreach (string file in files)
                {
                    if (Passes(file, policy, matcher))
                        yield return file;
                }

                string[] directories;
                try
                {
                    directories = Directory.GetDirectories(current);
                }
                catch (UnauthorizedAccessException)
                {
                    continue;
                }
                catch (IOException)
                {
                    continue;
                }

                foreach (string directory in directories)
                {
                    if (!matcher.IsDirectoryExcluded(directory))
                        pending.Push(directory);
                }
            }
        }

        private static bool Passes(string file, Policy policy, ExcludeMatcher matcher)
        {
            if (matcher.IsFileExcluded(file))
                return false;

            long length;
            try
            {
                length = new FileInfo(file).Length;
            }
            catch (UnauthorizedAccessException)
            {
                return false;
            }
            catch (IOException)
            {
                return false;
            }

            if (policy.MinFileSizeBytes > 0 && length < policy.MinFileSizeBytes)
                return false;
            if (policy.MaxFileSizeBytes > 0 && length > policy.MaxFileSizeBytes)
                return false;

            return true;
        }
    }
}
