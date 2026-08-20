namespace Armor.Tui
{
    using System;
    using System.Collections.Generic;
    using TUIKit.Ascii;

    /// <summary>
    /// The Armor ASCII-art wordmark and the startup splash text. The wordmark is rendered with TUIKit's
    /// built-in FIGlet engine so it is always correctly aligned.
    /// </summary>
    public static class ArmorBanner
    {
        /// <summary>
        /// Build the startup splash lines: the wordmark, a blank line, the version and copyright, a
        /// blank line, and the project URL. The modal appends its own "press any key" hint below these.
        /// </summary>
        /// <param name="version">The product version string (for example, <c>0.1.0</c>). May be null.</param>
        /// <returns>The splash content lines.</returns>
        public static IReadOnlyList<string> SplashLines(string version)
        {
            List<string> lines = new List<string>();
            foreach (string row in RenderWordmark())
                lines.Add(row);

            lines.Add(string.Empty);
            lines.Add("v" + (String.IsNullOrEmpty(version) ? "0.1.0" : version) + " Alpha - (c)2026 Joel Christner");
            lines.Add(string.Empty);
            lines.Add("https://github.com/jchristn/Armor");
            return lines;
        }

        private static IReadOnlyList<string> RenderWordmark()
        {
            try
            {
                IAsciiFont font = AsciiFontLibrary.Default.Get("Standard");
                return AsciiArt.Render("Armor", font);
            }
            catch (Exception)
            {
                return new List<string> { "A R M O R" };
            }
        }
    }
}
