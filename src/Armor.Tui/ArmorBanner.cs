namespace Armor.Tui
{
    using System;
    using System.Collections.Generic;
    using TUIKit.Ascii;
    using TUIKit.Ascii.Fonts;

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

        /// <summary>
        /// Render the "Armor" wordmark with the TUIKit Small font, padded so every row is the same
        /// width (so it centers cleanly and lays out in a fixed-width column). Falls back to plain text
        /// if the font engine is unavailable.
        /// </summary>
        /// <returns>The wordmark rows.</returns>
        public static string[] WordmarkLines()
        {
            List<string> rows = new List<string>();
            try
            {
                foreach (string row in AsciiArt.Render("armor", new SmallAsciiFont()))
                    rows.Add(row);
            }
            catch (Exception)
            {
                rows.Clear();
                rows.Add("a r m o r");
            }

            // The FIGlet font can include blank rows above and below the glyphs; drop them so the
            // wordmark has no leading blank line and occupies exactly its glyph height.
            while (rows.Count > 0 && rows[0].Trim().Length == 0)
                rows.RemoveAt(0);
            while (rows.Count > 0 && rows[rows.Count - 1].Trim().Length == 0)
                rows.RemoveAt(rows.Count - 1);
            if (rows.Count == 0)
                rows.Add("a r m o r");

            int width = 0;
            foreach (string row in rows)
                width = Math.Max(width, row.Length);
            for (int i = 0; i < rows.Count; i++)
                rows[i] = rows[i].PadRight(width);

            return rows.ToArray();
        }

        private static IReadOnlyList<string> RenderWordmark()
        {
            return WordmarkLines();
        }
    }
}
