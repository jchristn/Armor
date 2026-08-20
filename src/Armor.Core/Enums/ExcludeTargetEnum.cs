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
        Directory
    }
}
