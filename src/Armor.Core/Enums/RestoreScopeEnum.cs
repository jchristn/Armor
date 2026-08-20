namespace Armor.Core.Enums
{
    /// <summary>
    /// Identifies how much of a backup point-in-time a restore job reconstructs.
    /// </summary>
    public enum RestoreScopeEnum
    {
        /// <summary>
        /// Restore every file captured in the point-in-time.
        /// </summary>
        All,

        /// <summary>
        /// Restore a single folder and its descendants.
        /// </summary>
        Folder,

        /// <summary>
        /// Restore a single file.
        /// </summary>
        File
    }
}
