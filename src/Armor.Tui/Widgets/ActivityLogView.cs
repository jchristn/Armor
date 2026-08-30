namespace Armor.Tui.Widgets
{
    using System;
    using System.Collections.Generic;
    using System.Text;
    using TUIKit;
    using TUIKit.Input;
    using TUIKit.Unicode;
    using TUIKit.Widgets;

    /// <summary>
    /// The focusable activity log: a scrollback view of the timestamped, severity-tagged status lines the
    /// app and engine emit. It replaces the plain log pane so the user can Tab to it and act on the log.
    /// When focused: Up/Down and PageUp/PageDown scroll, Home/End jump to the oldest/newest line (End
    /// resumes following new lines), <c>c</c> copies the whole log to the system clipboard, and <c>x</c>
    /// (or Delete) clears it. Keys it does not use fall through so Tab and Escape still traverse the shell.
    /// New lines auto-follow only while the view is already at the bottom, so scrolling back to read is not
    /// yanked away by fresh output.
    /// </summary>
    public sealed class ActivityLogView : IWidget, IFocusable, IFocusAware
    {
        private const int MaxLines = 5000;
        private const byte DimColor = 8;
        private const byte TitleColor = 6;
        private const byte ErrorColor = 1;
        private const byte WarnColor = 3;

        private readonly List<string> _Lines = new List<string>();
        private int _ScrollFromBottom;
        private bool _Focused;

        /// <summary>
        /// Raised when the view wants the host to surface a short message (for example the result of a copy
        /// or clear). The host typically writes it back into the log via its status line.
        /// </summary>
        public event Action<string>? Announce;

        /// <summary>
        /// Append a line to the log. The oldest lines are dropped once the scrollback cap is reached. The
        /// view keeps following new lines only when it is already scrolled to the bottom.
        /// </summary>
        /// <param name="text">The line to append. Null is treated as an empty line.</param>
        public void WriteLine(string text)
        {
            _Lines.Add(text ?? String.Empty);
            if (_Lines.Count > MaxLines)
                _Lines.RemoveRange(0, _Lines.Count - MaxLines);
        }

        /// <summary>
        /// Remove every line from the log and return the view to the bottom.
        /// </summary>
        public void Clear()
        {
            _Lines.Clear();
            _ScrollFromBottom = 0;
        }

        /// <inheritdoc/>
        public void OnFocusChanged(bool focused)
        {
            _Focused = focused;
        }

        /// <inheritdoc/>
        public Size Measure(Size available)
        {
            return available;
        }

        /// <inheritdoc/>
        public bool HandleKey(KeyEvent key)
        {
            switch (key.Code)
            {
                case KeyCode.Up: Scroll(1); return true;
                case KeyCode.Down: Scroll(-1); return true;
                case KeyCode.PageUp: Scroll(10); return true;
                case KeyCode.PageDown: Scroll(-10); return true;
                case KeyCode.Home: _ScrollFromBottom = Math.Max(0, _Lines.Count - 1); return true;
                case KeyCode.End: _ScrollFromBottom = 0; return true;
                case KeyCode.Delete: ClearAndAnnounce(); return true;
                case KeyCode.Character:
                    switch (key.Rune)
                    {
                        case 'c':
                        case 'C':
                        case 'y':
                        case 'Y':
                            Copy();
                            return true;
                        case 'x':
                        case 'X':
                            ClearAndAnnounce();
                            return true;
                        default:
                            return false;
                    }
                default:
                    return false;
            }
        }

        /// <inheritdoc/>
        public void Render(ISurface surface)
        {
            if (surface == null)
                throw new ArgumentNullException(nameof(surface));

            int width = surface.Size.Width;
            int height = surface.Size.Height;
            if (width <= 0 || height <= 0)
                return;

            CellStyle baseStyle = CellStyle.Default;
            surface.Fill(new Rect(0, 0, width, height), Cell.Blank(baseStyle));

            // When focused, spend the top row on a shortcut hint; the log fills the rows beneath it.
            int firstLogRow = 0;
            if (_Focused)
            {
                string hint = "↑↓ scroll · PgUp/PgDn · Home/End · c copy · x clear · Tab out";
                surface.DrawText(0, 0, Clip(hint, width), baseStyle.WithForeground(Color.FromPalette(TitleColor)).WithAttribute(CellAttributes.Bold, true));
                firstLogRow = 1;
            }

            int visibleRows = height - firstLogRow;
            if (visibleRows <= 0)
                return;

            if (_Lines.Count == 0)
            {
                surface.DrawText(0, firstLogRow, Clip("(no activity yet)", width), baseStyle.WithForeground(Color.FromPalette(DimColor)));
                return;
            }

            // Clamp the scroll so it can never point past either end of the buffer.
            int maxScroll = Math.Max(0, _Lines.Count - visibleRows);
            if (_ScrollFromBottom > maxScroll)
                _ScrollFromBottom = maxScroll;
            if (_ScrollFromBottom < 0)
                _ScrollFromBottom = 0;

            // The bottom visible line is (count - 1 - scroll); fill upward from there.
            int bottomIndex = _Lines.Count - 1 - _ScrollFromBottom;
            int topIndex = Math.Max(0, bottomIndex - visibleRows + 1);
            int row = firstLogRow;
            for (int i = topIndex; i <= bottomIndex; i++)
            {
                surface.DrawText(0, row, Clip(_Lines[i], width), baseStyle.WithForeground(Color.FromPalette(SeverityColor(_Lines[i]))));
                row++;
            }

            // A small "more below" affordance when scrolled up, so it is clear there is newer output.
            if (_ScrollFromBottom > 0)
            {
                string more = "↓ " + _ScrollFromBottom + " newer";
                int x = width - Graphemes.MeasureWidth(more);
                if (x > 0)
                    surface.DrawText(x, height - 1, more, baseStyle.WithForeground(Color.FromPalette(DimColor)).WithAttribute(CellAttributes.Reverse, true));
            }
        }

        private void Scroll(int lines)
        {
            _ScrollFromBottom = Math.Max(0, _ScrollFromBottom + lines);
        }

        private void Copy()
        {
            if (_Lines.Count == 0)
            {
                Announce?.Invoke("The activity log is empty; nothing to copy.");
                return;
            }

            StringBuilder builder = new StringBuilder();
            foreach (string line in _Lines)
                builder.Append(line).Append('\n');

            bool ok = TextClipboard.TrySetText(builder.ToString());
            Announce?.Invoke(ok
                ? "Copied " + _Lines.Count + " activity-log line" + (_Lines.Count == 1 ? "" : "s") + " to the clipboard."
                : "Could not reach the system clipboard tool (clip/pbcopy/xclip). The full log is also at ~/.armor/logs.");
        }

        private void ClearAndAnnounce()
        {
            Clear();
            Announce?.Invoke("Activity log cleared.");
        }

        private static byte SeverityColor(string line)
        {
            if (line.Contains("[ERROR]", StringComparison.Ordinal))
                return ErrorColor;
            if (line.Contains("[WARN]", StringComparison.Ordinal))
                return WarnColor;
            if (line.Contains("[DEBUG]", StringComparison.Ordinal))
                return DimColor;
            return 7; // Default foreground for INFO and anything untagged.
        }

        private static string Clip(string text, int maxWidth)
        {
            if (maxWidth <= 0)
                return String.Empty;
            if (Graphemes.MeasureWidth(text) <= maxWidth)
                return text;

            int width = 0;
            int i = 0;
            while (i < text.Length)
            {
                int w = Graphemes.MeasureWidth(text[i].ToString());
                if (width + w > maxWidth)
                    break;
                width += w;
                i++;
            }
            return text.Substring(0, i);
        }
    }
}
