namespace Armor.Tui
{
    using System.Collections.Generic;

    /// <summary>
    /// The Armor ASCII-art wordmark and the startup splash text.
    /// </summary>
    public static class ArmorBanner
    {
        private static readonly string[] Art =
        {
            "    _    ____  __  __  ___  ____",
            "   / \\  |  _ \\|  \\/  |/ _ \\|  _ \\",
            "  / _ \\ | |_) | |\\/| | | | | |_) |",
            " / ___ \\|  _ <| |  | | |_| |  _ <",
            "/_/   \\_\\_| \\_\\_|  |_|\\___/|_| \\_\\"
        };

        /// <summary>
        /// Build the startup splash lines: the wordmark, a tagline with the version, the copyright, and
        /// the project URL.
        /// </summary>
        /// <param name="version">The product version string. May be null.</param>
        /// <returns>The splash content lines.</returns>
        public static IReadOnlyList<string> SplashLines(string version)
        {
            List<string> lines = new List<string>();
            foreach (string row in Art)
                lines.Add(row);

            lines.Add(string.Empty);
            lines.Add("Data protection for the paranoid  ·  v" + (version ?? string.Empty));
            lines.Add("(c)2026 Joel Christner");
            lines.Add(string.Empty);
            lines.Add("https://github.com/jchristn/Armor");
            return lines;
        }
    }
}
