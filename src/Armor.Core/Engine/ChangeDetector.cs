namespace Armor.Core.Engine
{
    using System;
    using System.IO;
    using Armor.Core.Models;

    /// <summary>
    /// Decides whether a source file has changed relative to a baseline manifest entry, using file
    /// size and last-modified timestamp, and optionally the Windows archive bit. Timestamp comparison
    /// allows a small tolerance to absorb filesystem precision differences. This type is stateless and
    /// thread-safe.
    /// </summary>
    public sealed class ChangeDetector
    {
        private readonly double _TimestampToleranceSeconds = 2.0;

        /// <summary>
        /// Timestamp comparison tolerance in seconds. Default is 2. Two timestamps within this many
        /// seconds are treated as equal. Minimum is 0.
        /// </summary>
        public double TimestampToleranceSeconds
        {
            get { return _TimestampToleranceSeconds; }
        }

        /// <summary>
        /// Determine whether a file has changed relative to a baseline entry.
        /// </summary>
        /// <param name="file">The current file. Cannot be null.</param>
        /// <param name="baseline">The baseline manifest entry, or null when there is no baseline.</param>
        /// <param name="useArchiveBit">Whether to consult the Windows archive bit as an additional signal.</param>
        /// <returns>True if the file should be treated as changed; otherwise false.</returns>
        /// <exception cref="ArgumentNullException">Thrown when <paramref name="file"/> is null.</exception>
        public bool HasChanged(FileInfo file, ManifestFileEntry? baseline, bool useArchiveBit)
        {
            if (file == null)
                throw new ArgumentNullException(nameof(file));
            if (baseline == null)
                return true;

            if (file.Length != baseline.SizeBytes)
                return true;

            double deltaSeconds = Math.Abs((file.LastWriteTimeUtc - baseline.ModifiedUtc).TotalSeconds);
            if (deltaSeconds > _TimestampToleranceSeconds)
                return true;

            if (useArchiveBit && OperatingSystem.IsWindows() && IsArchiveBitSet(file.FullName))
                return true;

            return false;
        }

        /// <summary>
        /// Determine whether a file's archive bit is set. Always false on non-Windows platforms.
        /// </summary>
        /// <param name="path">The file path. Cannot be null or whitespace.</param>
        /// <returns>True if the archive bit is set; otherwise false.</returns>
        /// <exception cref="ArgumentNullException">Thrown when <paramref name="path"/> is null or whitespace.</exception>
        public bool IsArchiveBitSet(string path)
        {
            if (String.IsNullOrWhiteSpace(path))
                throw new ArgumentNullException(nameof(path));
            if (!OperatingSystem.IsWindows())
                return false;

            try
            {
                return (File.GetAttributes(path) & FileAttributes.Archive) == FileAttributes.Archive;
            }
            catch (IOException)
            {
                return false;
            }
            catch (UnauthorizedAccessException)
            {
                return false;
            }
        }

        /// <summary>
        /// Clear a file's archive bit. No effect on non-Windows platforms or when the bit is already
        /// clear. Errors are swallowed so a metadata failure does not abort a backup.
        /// </summary>
        /// <param name="path">The file path. Cannot be null or whitespace.</param>
        /// <exception cref="ArgumentNullException">Thrown when <paramref name="path"/> is null or whitespace.</exception>
        public void ClearArchiveBit(string path)
        {
            if (String.IsNullOrWhiteSpace(path))
                throw new ArgumentNullException(nameof(path));
            if (!OperatingSystem.IsWindows())
                return;

            try
            {
                FileAttributes attributes = File.GetAttributes(path);
                if ((attributes & FileAttributes.Archive) == FileAttributes.Archive)
                    File.SetAttributes(path, attributes & ~FileAttributes.Archive);
            }
            catch (IOException)
            {
            }
            catch (UnauthorizedAccessException)
            {
            }
        }
    }
}
