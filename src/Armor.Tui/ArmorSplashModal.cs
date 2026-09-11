namespace Armor.Tui
{
    using System;
    using System.Collections.Generic;
    using TUIKit;
    using TUIKit.Input;
    using TUIKit.Modals;
    using TUIKit.Unicode;

    /// <summary>
    /// A modal that renders multi-line content verbatim (no reflow) in a centered bordered box, with an
    /// optional dimmed hint line at the bottom. Used for the startup splash. Any key dismisses it, so it
    /// never blocks the user from reaching the menu. Any http/https URL in the content is drawn as an
    /// underlined link and, when a <see cref="Links"/> registry is assigned, registered as a clickable hit
    /// region so the host can open it in the browser.
    /// </summary>
    public sealed class ArmorSplashModal : Modal
    {
        private const int PadX = 3;
        private const int PadY = 1;
        private const byte LinkColor = 6;

        private static readonly LinkScanner UrlScanner = new LinkScanner();

        private readonly string _Title;
        private readonly IReadOnlyList<string> _Lines;
        private readonly string _Hint;
        private readonly bool _Centered;

        /// <summary>
        /// An optional link registry the modal rebuilds each frame with the on-screen rectangle of every
        /// http/https URL in its content, so the host can hit-test a click against it and open the URL.
        /// Null (the default) disables clickable links; the URLs are still drawn as underlined text.
        /// </summary>
        public LinkRegistry? Links { get; set; }

        /// <summary>
        /// Initializes a new instance of the <see cref="ArmorSplashModal"/> class.
        /// </summary>
        /// <param name="title">The box title. May be empty.</param>
        /// <param name="lines">The content lines, rendered verbatim. Cannot be null.</param>
        /// <param name="hint">An optional dimmed footer hint; empty to omit.</param>
        /// <param name="centered">When true, each content line and the hint are horizontally centered.</param>
        /// <exception cref="ArgumentNullException">Thrown when <paramref name="lines"/> is null.</exception>
        public ArmorSplashModal(string title, IReadOnlyList<string> lines, string hint = "Press any key to start", bool centered = true)
        {
            _Title = title ?? string.Empty;
            _Lines = lines ?? throw new ArgumentNullException(nameof(lines));
            _Hint = hint ?? string.Empty;
            _Centered = centered;
        }

        /// <summary>
        /// Dismiss the splash on any key press.
        /// </summary>
        /// <param name="key">The key event.</param>
        /// <returns>Always true; the key is consumed and the modal closes.</returns>
        public override bool HandleKey(KeyEvent key)
        {
            Close(0);
            return true;
        }

        /// <summary>
        /// Render the centered splash box.
        /// </summary>
        /// <param name="surface">The surface to draw on. Cannot be null.</param>
        /// <exception cref="ArgumentNullException">Thrown when <paramref name="surface"/> is null.</exception>
        public override void Render(ISurface surface)
        {
            if (surface == null)
                throw new ArgumentNullException(nameof(surface));

            int screenWidth = surface.Size.Width;
            int screenHeight = surface.Size.Height;

            int contentWidth = Measure(_Title);
            for (int i = 0; i < _Lines.Count; i++)
                contentWidth = Math.Max(contentWidth, Measure(_Lines[i]));
            if (_Hint.Length > 0)
                contentWidth = Math.Max(contentWidth, Measure(_Hint));

            contentWidth = Math.Max(4, Math.Min(contentWidth, screenWidth - 2 - (2 * PadX)));

            int hintRows = _Hint.Length > 0 ? 2 : 0;
            int contentHeight = _Lines.Count + hintRows;
            int boxWidth = Math.Min(screenWidth, contentWidth + 2 + (2 * PadX));
            int boxHeight = Math.Min(screenHeight, contentHeight + 2 + (2 * PadY));

            int boxX = Math.Max(0, (screenWidth - boxWidth) / 2);
            int boxY = Math.Max(0, (screenHeight - boxHeight) / 2);
            Rect box = new Rect(boxX, boxY, boxWidth, boxHeight);

            surface.Fill(box, Cell.Blank(CellStyle.Default));
            surface.DrawBox(box, CellStyle.Default.WithForeground(Color.FromPalette(6)), _Title);

            int contentX = boxX + 1 + PadX;
            int firstRow = boxY + 1 + PadY;
            int lastContentRow = boxY + boxHeight - 2 - PadY;

            // The link registry is rebuilt from scratch every frame so it always reflects the current
            // layout (the box re-centers on resize) and holds no links scrolled or resized off screen.
            Links?.Clear();

            for (int i = 0; i < _Lines.Count; i++)
            {
                int row = firstRow + i;
                if (row > lastContentRow)
                    break;
                DrawContentLine(surface, LineX(contentX, contentWidth, _Lines[i]), row, _Lines[i]);
            }

            if (_Hint.Length > 0)
            {
                int hintRow = lastContentRow;
                if (hintRow > firstRow + _Lines.Count - 1)
                    surface.DrawText(LineX(contentX, contentWidth, _Hint), hintRow, _Hint, CellStyle.Default.WithForeground(Color.FromPalette(8)));
            }
        }

        // Draw one content line, drawing any http/https URLs it contains as underlined link text (and, when
        // a registry is present, registering their on-screen rectangle so a click can open them). URLs are
        // ASCII, so a character index within the line is also its column offset from the line's start.
        private void DrawContentLine(ISurface surface, int lineX, int row, string line)
        {
            surface.DrawText(lineX, row, line, CellStyle.Default);

            IReadOnlyList<LinkMatch> matches = UrlScanner.Scan(line);
            if (matches.Count == 0)
                return;

            CellStyle linkStyle = CellStyle.Default.WithForeground(Color.FromPalette(LinkColor)).WithAttribute(CellAttributes.Underline, true);
            foreach (LinkMatch match in matches)
            {
                int linkX = lineX + match.Start;
                surface.DrawText(linkX, row, match.Uri, linkStyle);
                Links?.Add(match.Uri, new Rect(linkX, row, match.Length, 1), match.Uri, null, null);
            }
        }

        private int LineX(int contentX, int contentWidth, string line)
        {
            if (!_Centered)
                return contentX;
            return contentX + Math.Max(0, (contentWidth - Measure(line)) / 2);
        }

        private static int Measure(string text)
        {
            return Graphemes.MeasureWidth(text ?? string.Empty);
        }
    }
}
