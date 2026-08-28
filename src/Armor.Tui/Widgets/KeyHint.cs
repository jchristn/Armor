namespace Armor.Tui.Widgets
{
    using System;

    /// <summary>
    /// One keyboard-shortcut hint: a key (or key combo) and the short action it performs. Rendered the same
    /// way in the bottom shortcut bar and under a workspace title — the key in the accent color, the label
    /// dimmed — so shortcut hints look the same everywhere.
    /// </summary>
    public readonly struct KeyHint
    {
        /// <summary>
        /// Initializes a new instance of the <see cref="KeyHint"/> struct.
        /// </summary>
        /// <param name="key">The key or key combo (for example <c>↑↓</c>, <c>↵</c>, <c>^Q</c>).</param>
        /// <param name="label">The short action label.</param>
        public KeyHint(string key, string label)
        {
            Key = key ?? String.Empty;
            Label = label ?? String.Empty;
        }

        /// <summary>The key or key combo.</summary>
        public string Key { get; }

        /// <summary>The short action label.</summary>
        public string Label { get; }
    }
}
