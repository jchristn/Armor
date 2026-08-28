namespace Armor.Core.Models
{
    using System.Collections.Generic;
    using Armor.Core.Enums;

    /// <summary>
    /// The canonical set of exclude rules seeded into the shared global exclude list, and used by the
    /// TUI's "restore defaults" action. These are the directories that dominate a developer machine's
    /// file count — source-control metadata, build output, package and tool caches, and the user's
    /// <c>AppData</c> tree — none of which belong in a backup. Every entry is a bare name with
    /// <see cref="ExcludeTargetEnum.Any"/>, so it excludes both a file and a directory of that name and
    /// prunes a matching directory from the walk rather than descending into it. This type is stateless.
    /// </summary>
    public static class GlobalExcludeDefaults
    {
        // Bare directory/file names excluded everywhere. Kept as a single source of truth so the seeding
        // migration and the TUI's "restore defaults" action never drift apart.
        private static readonly string[] _Names =
        {
            // Core developer set — source control, build output, and package/tool caches.
            ".git", "bin", "obj", "debug", "release", "node_modules", ".vs", "packages", ".nuget",
            "AppData",

            // OS and cache junk — transient or system-owned directories that should never be backed up.
            "Temp", ".cache", "$RECYCLE.BIN", "System Volume Information",

            // Other-language build and dependency directories for polyglot repositories.
            "__pycache__", ".gradle", "target", "venv", ".venv", "dist", "build",
        };

        /// <summary>
        /// Build a fresh list of the default global exclude patterns. A new list of new instances is
        /// returned on each call so callers may mutate the result freely.
        /// </summary>
        /// <returns>The default exclude patterns.</returns>
        public static List<ExcludePattern> Create()
        {
            List<ExcludePattern> patterns = new List<ExcludePattern>(_Names.Length);
            foreach (string name in _Names)
                patterns.Add(new ExcludePattern(name, false, ExcludeTargetEnum.Any));
            return patterns;
        }
    }
}
