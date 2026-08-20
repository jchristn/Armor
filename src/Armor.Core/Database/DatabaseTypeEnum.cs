namespace Armor.Core.Database
{
    /// <summary>
    /// Database provider types Armor can target. Armor ships with SQLite; the enumeration exists so
    /// the data-layer abstraction can grow additional providers without changing its shape.
    /// </summary>
    public enum DatabaseTypeEnum
    {
        /// <summary>
        /// Local SQLite database.
        /// </summary>
        Sqlite
    }
}
