namespace Armor.Core.Helpers
{
    using System;
    using System.Collections.Generic;
    using System.IO;
    using Armor.Core.Exceptions;

    /// <summary>
    /// Maps a file path captured in a backup manifest to the path a restore writes it to. Manifest paths
    /// are stored exactly as the source machine reported them, so a backup taken on Windows carries paths
    /// like <c>C:\Users\me\file.txt</c> even when it is restored on macOS or Linux, where a backslash is an
    /// ordinary filename character rather than a separator. The mapper recognizes Windows-shaped paths
    /// (drive-letter, UNC, and <c>\\?\</c> forms) on every platform, so the captured folder structure is
    /// recreated under the destination folder instead of being flattened into one long filename.
    /// </summary>
    public static class RestorePathMapper
    {
        private static readonly char[] _AllSeparators = new[] { '\\', '/' };
        private static readonly char[] _PosixSeparators = new[] { '/' };

        /// <summary>
        /// Map a manifest path to its restore destination.
        /// </summary>
        /// <param name="sourcePath">The path as captured in the manifest. Cannot be null or empty.</param>
        /// <param name="destinationRoot">The folder to restore into, or null/empty to restore each file to its original location.</param>
        /// <returns>The local path to write the file to.</returns>
        /// <exception cref="ArgumentNullException">Thrown when <paramref name="sourcePath"/> is null or empty.</exception>
        /// <exception cref="ArmorException">
        /// Thrown when a Windows path is restored in place on a non-Windows machine, or when the path has no
        /// file name or would escape the destination folder.
        /// </exception>
        public static string MapDestination(string sourcePath, string? destinationRoot)
        {
            if (String.IsNullOrEmpty(sourcePath))
                throw new ArgumentNullException(nameof(sourcePath));

            bool windowsShaped = IsWindowsPath(sourcePath);

            if (String.IsNullOrWhiteSpace(destinationRoot))
            {
                if (windowsShaped && !OperatingSystem.IsWindows())
                    throw new ArmorException("'" + sourcePath + "' was backed up on Windows and cannot be restored to its original location on this machine. Restore it into a folder instead.");
                return sourcePath;
            }

            // A backslash only separates folders in a Windows path (or on Windows itself); in a path captured
            // on macOS/Linux it is a legal filename character and must be preserved.
            string relative = windowsShaped ? StripWindowsRoot(sourcePath) : StripLocalRoot(sourcePath);
            char[] separators = windowsShaped || OperatingSystem.IsWindows() ? _AllSeparators : _PosixSeparators;

            List<string> segments = new List<string>();
            foreach (string segment in relative.Split(separators, StringSplitOptions.RemoveEmptyEntries))
            {
                if (segment == ".")
                    continue;
                if (segment == "..")
                    throw new ArmorException("'" + sourcePath + "' contains a parent-folder segment and cannot be restored safely.");
                segments.Add(segment);
            }

            if (segments.Count == 0)
                throw new ArmorException("'" + sourcePath + "' has no file name to restore.");

            segments.Insert(0, destinationRoot!);
            return Path.Combine(segments.ToArray());
        }

        /// <summary>
        /// Whether a path is an absolute Windows path: <c>C:\...</c>, <c>C:/...</c>, <c>\\server\share\...</c>,
        /// or a <c>\\?\</c> / <c>\\.\</c> device path. The check is purely textual, so it gives the same answer
        /// on every platform.
        /// </summary>
        /// <param name="path">The path to inspect.</param>
        /// <returns>True when the path is Windows-shaped.</returns>
        public static bool IsWindowsPath(string? path)
        {
            if (String.IsNullOrEmpty(path))
                return false;
            if (path.Length >= 2 && path[0] == '\\' && path[1] == '\\')
                return true;
            return path.Length >= 3 && IsDriveLetter(path[0]) && path[1] == ':' && (path[2] == '\\' || path[2] == '/');
        }

        private static string StripWindowsRoot(string path)
        {
            // Device paths: \\?\C:\dir, \\.\C:\dir, \\?\UNC\server\share\dir.
            if (path.Length >= 4 && path[0] == '\\' && path[1] == '\\' && (path[2] == '?' || path[2] == '.') && path[3] == '\\')
            {
                string rest = path.Substring(4);
                if (rest.StartsWith("UNC\\", StringComparison.OrdinalIgnoreCase))
                    return StripUncRoot(rest.Substring(4));
                return StripWindowsRoot(rest);
            }

            // UNC: \\server\share\dir — the server and share form the root, as on Windows.
            if (path.Length >= 2 && path[0] == '\\' && path[1] == '\\')
                return StripUncRoot(path.Substring(2));

            // Drive letter: C:\dir.
            if (path.Length >= 2 && IsDriveLetter(path[0]) && path[1] == ':')
                return path.Substring(2);

            return path;
        }

        private static string StripUncRoot(string afterLeadingSlashes)
        {
            string[] parts = afterLeadingSlashes.Split(_AllSeparators, 3);
            return parts.Length == 3 ? parts[2] : String.Empty;
        }

        private static string StripLocalRoot(string path)
        {
            string root = Path.GetPathRoot(path) ?? String.Empty;
            return root.Length > 0 && path.StartsWith(root, StringComparison.Ordinal)
                ? path.Substring(root.Length)
                : path;
        }

        private static bool IsDriveLetter(char c)
        {
            return (c >= 'A' && c <= 'Z') || (c >= 'a' && c <= 'z');
        }
    }
}
