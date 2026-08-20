namespace Armor.Core.Backup
{
    using System;
    using System.IO;
    using System.IO.Compression;
    using System.Threading;
    using System.Threading.Tasks;

    /// <summary>
    /// Bundles Armor's own configuration file, SQLite database, and state directory into a single zip
    /// archive, and restores them from it. This lets a user move Armor to a new machine and resume
    /// restoring their data. The database should be checkpointed or closed before export so the copied
    /// file is complete. This type is stateless and thread-safe.
    /// </summary>
    public static class ConfigBackup
    {
        private const string ConfigEntryName = "config.json";
        private const string DatabaseEntryName = "database.db";
        private const string StatePrefix = "state/";

        /// <summary>
        /// Export the configuration file, database, and state directory into a zip archive.
        /// </summary>
        /// <param name="configFilePath">Path to the configuration file. Included when it exists.</param>
        /// <param name="databaseFilePath">Path to the SQLite database file. Included when it exists.</param>
        /// <param name="stateDirectory">Path to the state directory. Its files are included when it exists.</param>
        /// <param name="destinationZipPath">Path of the zip archive to create. Cannot be null or whitespace.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>A task that completes when the archive is written.</returns>
        /// <exception cref="ArgumentNullException">Thrown when a required path is null or whitespace.</exception>
        public static async Task ExportAsync(string configFilePath, string databaseFilePath, string stateDirectory, string destinationZipPath, CancellationToken token = default)
        {
            if (String.IsNullOrWhiteSpace(configFilePath))
                throw new ArgumentNullException(nameof(configFilePath));
            if (String.IsNullOrWhiteSpace(databaseFilePath))
                throw new ArgumentNullException(nameof(databaseFilePath));
            if (String.IsNullOrWhiteSpace(stateDirectory))
                throw new ArgumentNullException(nameof(stateDirectory));
            if (String.IsNullOrWhiteSpace(destinationZipPath))
                throw new ArgumentNullException(nameof(destinationZipPath));

            string? destinationDirectory = Path.GetDirectoryName(Path.GetFullPath(destinationZipPath));
            if (!String.IsNullOrEmpty(destinationDirectory))
                Directory.CreateDirectory(destinationDirectory);

            using (FileStream zipStream = new FileStream(destinationZipPath, FileMode.Create, FileAccess.Write, FileShare.None))
            using (ZipArchive archive = new ZipArchive(zipStream, ZipArchiveMode.Create))
            {
                await AddFileAsync(archive, configFilePath, ConfigEntryName, token).ConfigureAwait(false);
                await AddFileAsync(archive, databaseFilePath, DatabaseEntryName, token).ConfigureAwait(false);

                if (Directory.Exists(stateDirectory))
                {
                    string fullState = Path.GetFullPath(stateDirectory);
                    foreach (string file in Directory.EnumerateFiles(fullState, "*", SearchOption.AllDirectories))
                    {
                        token.ThrowIfCancellationRequested();
                        string relative = Path.GetRelativePath(fullState, file).Replace('\\', '/');
                        await AddFileAsync(archive, file, StatePrefix + relative, token).ConfigureAwait(false);
                    }
                }
            }
        }

        /// <summary>
        /// Restore the configuration file, database, and state directory from a zip archive produced by
        /// <see cref="ExportAsync"/>, overwriting existing files.
        /// </summary>
        /// <param name="sourceZipPath">Path to the zip archive. Cannot be null or whitespace.</param>
        /// <param name="configFilePath">Destination path for the configuration file. Cannot be null or whitespace.</param>
        /// <param name="databaseFilePath">Destination path for the database file. Cannot be null or whitespace.</param>
        /// <param name="stateDirectory">Destination state directory. Cannot be null or whitespace.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>A task that completes when restoration is finished.</returns>
        /// <exception cref="ArgumentNullException">Thrown when a required path is null or whitespace.</exception>
        /// <exception cref="FileNotFoundException">Thrown when the archive does not exist.</exception>
        public static async Task ImportAsync(string sourceZipPath, string configFilePath, string databaseFilePath, string stateDirectory, CancellationToken token = default)
        {
            if (String.IsNullOrWhiteSpace(sourceZipPath))
                throw new ArgumentNullException(nameof(sourceZipPath));
            if (String.IsNullOrWhiteSpace(configFilePath))
                throw new ArgumentNullException(nameof(configFilePath));
            if (String.IsNullOrWhiteSpace(databaseFilePath))
                throw new ArgumentNullException(nameof(databaseFilePath));
            if (String.IsNullOrWhiteSpace(stateDirectory))
                throw new ArgumentNullException(nameof(stateDirectory));
            if (!File.Exists(sourceZipPath))
                throw new FileNotFoundException("Backup archive not found.", sourceZipPath);

            using (FileStream zipStream = new FileStream(sourceZipPath, FileMode.Open, FileAccess.Read, FileShare.Read))
            using (ZipArchive archive = new ZipArchive(zipStream, ZipArchiveMode.Read))
            {
                foreach (ZipArchiveEntry entry in archive.Entries)
                {
                    token.ThrowIfCancellationRequested();
                    if (String.IsNullOrEmpty(entry.Name))
                        continue;

                    string destination = ResolveDestination(entry.FullName, configFilePath, databaseFilePath, stateDirectory);
                    string? directory = Path.GetDirectoryName(destination);
                    if (!String.IsNullOrEmpty(directory))
                        Directory.CreateDirectory(directory);

                    using (Stream entryStream = entry.Open())
                    using (FileStream output = new FileStream(destination, FileMode.Create, FileAccess.Write, FileShare.None))
                    {
                        await entryStream.CopyToAsync(output, token).ConfigureAwait(false);
                    }
                }
            }
        }

        private static string ResolveDestination(string entryName, string configFilePath, string databaseFilePath, string stateDirectory)
        {
            string normalized = entryName.Replace('\\', '/');
            if (String.Equals(normalized, ConfigEntryName, StringComparison.Ordinal))
                return configFilePath;
            if (String.Equals(normalized, DatabaseEntryName, StringComparison.Ordinal))
                return databaseFilePath;
            if (normalized.StartsWith(StatePrefix, StringComparison.Ordinal))
            {
                string relative = normalized.Substring(StatePrefix.Length);
                return Path.Combine(stateDirectory, relative.Replace('/', Path.DirectorySeparatorChar));
            }
            return Path.Combine(stateDirectory, normalized.Replace('/', Path.DirectorySeparatorChar));
        }

        private static async Task AddFileAsync(ZipArchive archive, string filePath, string entryName, CancellationToken token)
        {
            if (!File.Exists(filePath))
                return;

            ZipArchiveEntry entry = archive.CreateEntry(entryName, CompressionLevel.Optimal);
            using (Stream entryStream = entry.Open())
            using (FileStream source = new FileStream(filePath, FileMode.Open, FileAccess.Read, FileShare.Read))
            {
                await source.CopyToAsync(entryStream, token).ConfigureAwait(false);
            }
        }
    }
}
