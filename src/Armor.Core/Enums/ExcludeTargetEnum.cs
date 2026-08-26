namespace Armor.Core.Enums
{
    /// <summary>
    /// Identifies whether an exclude pattern applies to files or to directories.
    /// </summary>
    public enum ExcludeTargetEnum
    {
        /// <summary>
        /// The pattern is matched against file names or paths.
        /// </summary>
        File,

        /// <summary>
        /// The pattern is matched against directory names or paths.
        /// </summary>
        Directory,

        /// <summary>
        /// The pattern is matched against both files and directories: a file with the name (or matching
        /// path) is excluded, and a directory with the name (or matching path) is pruned from the walk.
        /// This is the natural meaning of a bare name such as <c>.git</c> or <c>node_modules</c>, which a
        /// user expects to exclude anything of that name regardless of whether it is a file or a folder.
        /// </summary>
        Any
    }
}
