namespace Armor.Tui.Widgets
{
    using System;
    using System.Collections.Generic;
    using TUIKit;
    using TUIKit.Widgets;

    /// <summary>
    /// A persistent single-row key-hint bar drawn at the bottom of the dashboard, so the essential
    /// shortcuts are always on screen rather than hidden behind F1. Each hint is a key drawn in an
    /// accent color followed by a short label. When the terminal is too narrow to show every hint, the
    /// bar keeps as many leading hints as fit and always preserves the trailing "F1 Help" hint, so the
    /// full reference is reachable no matter how small the window.
    /// </summary>
    public sealed class FooterHints : IWidget
    {
        private const byte KeyColor = 6;    // cyan
        private const byte LabelColor = 8;  // dim gray
        private const string Separator = "  ";

        private readonly List<(string Key, string Label)> _Hints;
        private readonly (string Key, string Label) _Help = ("F1", "Help");

        /// <summary>
        /// Initializes a new instance of the <see cref="FooterHints"/> class with the default hint set.
        /// </summary>
        public FooterHints()
        {
            // Ordered by how useful each is to a first-time user discovering the app. "F1 Help" is not in
            // this list — it is always appended last (and preserved when space is tight) by Render.
            _Hints = new List<(string, string)>
            {
                ("↑↓", "Move"),
                ("↵", "Run"),
                ("c", "New"),
                ("e", "Edit"),
                ("d", "Delete"),
                ("r", "Restore"),
                ("g", "Globals"),
                ("s", "Stats"),
                ("x", "Export"),
                ("F5", "Refresh"),
                ("^Q", "Quit"),
            };
        }

        /// <inheritdoc/>
        public Size Measure(Size available)
        {
            return new Size(available.Width, 1);
        }

        /// <inheritdoc/>
        public void Render(ISurface surface)
        {
            if (surface == null)
                throw new ArgumentNullException(nameof(surface));

            int width = surface.Size.Width;
            int height = surface.Size.Height;
            if (width < 2 || height < 1)
                return;

            CellStyle baseStyle = CellStyle.Default;
            surface.Fill(new Rect(0, 0, width, height), Cell.Blank(baseStyle));

            CellStyle keyStyle = baseStyle.WithForeground(Color.FromPalette(KeyColor)).WithAttribute(CellAttributes.Bold, true);
            CellStyle labelStyle = baseStyle.WithForeground(Color.FromPalette(LabelColor));

            // Reserve room for the trailing "F1 Help" hint so it is never truncated away. Everything else
            // is drawn only while it fits before that reserved tail.
            int helpWidth = Separator.Length + HintWidth(_Help);
            int budget = width - helpWidth;

            int x = 0;
            bool first = true;
            foreach ((string Key, string Label) hint in _Hints)
            {
                int needed = (first ? 0 : Separator.Length) + HintWidth(hint);
                if (x + needed > budget)
                    break;
                if (!first)
                    x += DrawText(surface, x, Separator, labelStyle);
                x = DrawHint(surface, x, hint, keyStyle, labelStyle);
                first = false;
            }

            // The reserved "F1 Help" hint, right-aligned against the space set aside for it.
            int helpX = Math.Max(x + (first ? 0 : Separator.Length), width - helpWidth + Separator.Length);
            if (!first)
                DrawText(surface, helpX - Separator.Length, Separator, labelStyle);
            DrawHint(surface, helpX, _Help, keyStyle, labelStyle);
        }

        private static int HintWidth((string Key, string Label) hint)
        {
            return hint.Key.Length + 1 + hint.Label.Length;
        }

        private static int DrawHint(ISurface surface, int x, (string Key, string Label) hint, CellStyle keyStyle, CellStyle labelStyle)
        {
            x += DrawText(surface, x, hint.Key, keyStyle);
            x += DrawText(surface, x, " " + hint.Label, labelStyle);
            return x;
        }

        // Draw text clipped to the surface width — the surface does not clip on its own — and return the
        // number of columns actually advanced.
        private static int DrawText(ISurface surface, int x, string text, CellStyle style)
        {
            int width = surface.Size.Width;
            if (x >= width || string.IsNullOrEmpty(text))
                return text?.Length ?? 0;
            string clipped = x + text.Length > width ? text.Substring(0, width - x) : text;
            surface.DrawText(x, 0, clipped, style);
            return text.Length;
        }
    }
}
