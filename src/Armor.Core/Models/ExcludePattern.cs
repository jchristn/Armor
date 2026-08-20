namespace Armor.Core.Models
{
    using System;
    using Armor.Core.Enums;

    /// <summary>
    /// A single exclude rule attached to a policy. A pattern is either a wildcard (glob) or a
    /// regular expression, and applies either to files or to directories.
    /// </summary>
    public class ExcludePattern
    {
        private string _Pattern = String.Empty;

        /// <summary>
        /// The pattern text. For a wildcard this is a glob such as <c>*.tmp</c>; for a regular
        /// expression this is a .NET regex. Cannot be null or whitespace.
        /// </summary>
        /// <exception cref="ArgumentNullException">Thrown when set to null or whitespace.</exception>
        public string Pattern
        {
            get
            {
                return _Pattern;
            }
            set
            {
                if (String.IsNullOrWhiteSpace(value))
                    throw new ArgumentNullException(nameof(Pattern));
                _Pattern = value;
            }
        }

        /// <summary>
        /// When true, <see cref="Pattern"/> is interpreted as a regular expression; when false, as a
        /// wildcard glob. Default is false.
        /// </summary>
        public bool IsRegex { get; set; } = false;

        /// <summary>
        /// Whether the pattern applies to files or directories. Default is
        /// <see cref="ExcludeTargetEnum.File"/>.
        /// </summary>
        public ExcludeTargetEnum Target { get; set; } = ExcludeTargetEnum.File;

        /// <summary>
        /// Initializes a new instance of the <see cref="ExcludePattern"/> class.
        /// </summary>
        public ExcludePattern()
        {
        }

        /// <summary>
        /// Initializes a new instance of the <see cref="ExcludePattern"/> class.
        /// </summary>
        /// <param name="pattern">The pattern text. Cannot be null or whitespace.</param>
        /// <param name="isRegex">Whether the pattern is a regular expression.</param>
        /// <param name="target">Whether the pattern applies to files or directories.</param>
        /// <exception cref="ArgumentNullException">Thrown when <paramref name="pattern"/> is null or whitespace.</exception>
        public ExcludePattern(string pattern, bool isRegex, ExcludeTargetEnum target)
        {
            Pattern = pattern;
            IsRegex = isRegex;
            Target = target;
        }
    }
}
