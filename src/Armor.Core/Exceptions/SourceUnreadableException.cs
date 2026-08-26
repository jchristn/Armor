namespace Armor.Core.Exceptions
{
    using System;

    /// <summary>
    /// Thrown when a source file cannot be opened or read during a backup — for example it is locked with
    /// no shared read, the process lacks permission, or it is a broken reparse point. The backup engine
    /// catches this to skip the single file and continue rather than aborting the whole run.
    /// </summary>
    public sealed class SourceUnreadableException : ArmorException
    {
        private string _Path = String.Empty;

        /// <summary>
        /// Initializes a new instance of the <see cref="SourceUnreadableException"/> class.
        /// </summary>
        /// <param name="path">The absolute path of the file that could not be read. Cannot be null or whitespace.</param>
        /// <param name="innerException">The underlying I/O or access exception.</param>
        /// <exception cref="ArgumentNullException">Thrown when <paramref name="path"/> is null or whitespace.</exception>
        public SourceUnreadableException(string path, Exception innerException)
            : base("Source file could not be read: '" + path + "'.", innerException)
        {
            if (String.IsNullOrWhiteSpace(path))
                throw new ArgumentNullException(nameof(path));
            _Path = path;
        }

        /// <summary>
        /// The absolute path of the file that could not be read.
        /// </summary>
        public string Path
        {
            get { return _Path; }
        }
    }
}
