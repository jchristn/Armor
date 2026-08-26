namespace Armor.Core.Engine
{
    using System;
    using System.Collections.Generic;
    using System.IO;
    using System.Text;
    using System.Text.RegularExpressions;
    using Armor.Core.Enums;
    using Armor.Core.Models;

    /// <summary>
    /// Matches file and directory paths against a policy's exclude patterns. Wildcard file patterns
    /// match a file's name; wildcard directory patterns match a directory's name; regular-expression
    /// patterns match the full path (with forward slashes). Matching is case-insensitive. This type is
    /// immutable after construction and thread-safe.
    /// </summary>
    public sealed class ExcludeMatcher
    {
        private readonly List<Regex> _FileNameGlobs = new List<Regex>();
        private readonly List<Regex> _FilePathRegexes = new List<Regex>();
        private readonly List<Regex> _DirectoryNameGlobs = new List<Regex>();
        private readonly List<Regex> _DirectoryPathRegexes = new List<Regex>();

        /// <summary>
        /// Initializes a new instance of the <see cref="ExcludeMatcher"/> class from a set of patterns.
        /// </summary>
        /// <param name="patterns">The exclude patterns. Cannot be null.</param>
        /// <exception cref="ArgumentNullException">Thrown when <paramref name="patterns"/> is null.</exception>
        /// <exception cref="ArgumentException">Thrown when a regular-expression pattern is invalid.</exception>
        public ExcludeMatcher(IEnumerable<ExcludePattern> patterns)
        {
            if (patterns == null)
                throw new ArgumentNullException(nameof(patterns));

            foreach (ExcludePattern pattern in patterns)
            {
                if (pattern == null)
                    continue;

                Regex compiled = pattern.IsRegex ? CompileRegex(pattern.Pattern) : CompileGlob(pattern.Pattern);

                // Any applies to both files and directories; File and Directory apply to just their own.
                bool appliesToFiles = pattern.Target != ExcludeTargetEnum.Directory;
                bool appliesToDirectories = pattern.Target != ExcludeTargetEnum.File;

                if (appliesToFiles)
                {
                    if (pattern.IsRegex)
                        _FilePathRegexes.Add(compiled);
                    else
                        _FileNameGlobs.Add(compiled);
                }
                if (appliesToDirectories)
                {
                    if (pattern.IsRegex)
                        _DirectoryPathRegexes.Add(compiled);
                    else
                        _DirectoryNameGlobs.Add(compiled);
                }
            }
        }

        /// <summary>
        /// Determine whether a file path is excluded.
        /// </summary>
        /// <param name="fullPath">The absolute file path. Cannot be null or whitespace.</param>
        /// <returns>True if the file is excluded; otherwise false.</returns>
        /// <exception cref="ArgumentNullException">Thrown when <paramref name="fullPath"/> is null or whitespace.</exception>
        public bool IsFileExcluded(string fullPath)
        {
            if (String.IsNullOrWhiteSpace(fullPath))
                throw new ArgumentNullException(nameof(fullPath));

            string name = Path.GetFileName(fullPath);
            string normalized = Normalize(fullPath);

            foreach (Regex glob in _FileNameGlobs)
                if (glob.IsMatch(name))
                    return true;
            foreach (Regex regex in _FilePathRegexes)
                if (regex.IsMatch(normalized))
                    return true;
            return false;
        }

        /// <summary>
        /// Determine whether a directory path is excluded (pruned from the walk).
        /// </summary>
        /// <param name="fullPath">The absolute directory path. Cannot be null or whitespace.</param>
        /// <returns>True if the directory is excluded; otherwise false.</returns>
        /// <exception cref="ArgumentNullException">Thrown when <paramref name="fullPath"/> is null or whitespace.</exception>
        public bool IsDirectoryExcluded(string fullPath)
        {
            if (String.IsNullOrWhiteSpace(fullPath))
                throw new ArgumentNullException(nameof(fullPath));

            string name = new DirectoryInfo(fullPath).Name;
            string normalized = Normalize(fullPath);

            foreach (Regex glob in _DirectoryNameGlobs)
                if (glob.IsMatch(name))
                    return true;
            foreach (Regex regex in _DirectoryPathRegexes)
                if (regex.IsMatch(normalized))
                    return true;
            return false;
        }

        private static string Normalize(string path)
        {
            return path.Replace('\\', '/');
        }

        private static Regex CompileRegex(string pattern)
        {
            try
            {
                return new Regex(pattern, RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
            }
            catch (ArgumentException ex)
            {
                throw new ArgumentException("Invalid exclude regular expression: '" + pattern + "'.", ex);
            }
        }

        private static Regex CompileGlob(string glob)
        {
            StringBuilder builder = new StringBuilder();
            builder.Append('^');
            foreach (char c in glob)
            {
                switch (c)
                {
                    case '*':
                        builder.Append(".*");
                        break;
                    case '?':
                        builder.Append('.');
                        break;
                    default:
                        builder.Append(Regex.Escape(c.ToString()));
                        break;
                }
            }
            builder.Append('$');
            return new Regex(builder.ToString(), RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
        }
    }
}
