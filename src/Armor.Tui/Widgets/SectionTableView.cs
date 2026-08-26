namespace Armor.Tui.Widgets
{
    using System;
    using System.Collections.Generic;
    using TUIKit;
    using TUIKit.Input;
    using TUIKit.Widgets;

    /// <summary>
    /// One row in a <see cref="SectionTableView"/>: the pre-formatted cell values plus an opaque tag
    /// the host uses to recover the underlying model object for the selected row.
    /// </summary>
    public sealed class TableRow
    {
        /// <summary>
        /// The cell values, one per column. Never null.
        /// </summary>
        public string[] Cells { get; }

        /// <summary>
        /// The underlying model object this row represents, or null.
        /// </summary>
        public object? Tag { get; }

        /// <summary>
        /// Initializes a new instance of the <see cref="TableRow"/> class.
        /// </summary>
        /// <param name="cells">The cell values. Cannot be null.</param>
        /// <param name="tag">The underlying model object, or null.</param>
        /// <exception cref="ArgumentNullException">Thrown when <paramref name="cells"/> is null.</exception>
        public TableRow(string[] cells, object? tag)
        {
            Cells = cells ?? throw new ArgumentNullException(nameof(cells));
            Tag = tag;
        }
    }

    /// <summary>
    /// A focusable, scrolling, single-select table widget. It renders an optional bold header row and a
    /// body of rows with the selection drawn as a highlight bar when focused. Arrow/Home/End/PageUp/
    /// PageDown move the selection; Enter raises <see cref="Activated"/>. Moving the selection raises
    /// <see cref="SelectionChanged"/>, so a caller can drive a detail view live from selection. Row
    /// action keys the widget does not consume (for example "c", "d") fall through to host commands.
    /// </summary>
    public sealed class SectionTableView : IWidget, IFocusable, IFocusAware
    {
        private readonly List<TableRow> _Rows = new List<TableRow>();
        private string[] _Headers;
        private int[] _Weights;
        private bool _ShowHeader;
        private string _EmptyMessage = "Nothing here yet.";
        private int _Selected;
        private int _ScrollTop;
        private bool _Focused;
        private string? _Title;
        private string? _Subtitle;
        private readonly int _PadLeft;
        private readonly int _PadRight;
        private readonly bool _Bordered;

        private const byte HeaderColor = 6;      // cyan
        private const byte SelectedBg = 6;       // cyan bar
        private const byte SelectedFg = 0;       // black text on the bar
        private const byte DimColor = 8;         // gray

        /// <summary>
        /// Raised after the selected row changes (by key or by <see cref="SetRows"/>).
        /// </summary>
        public event Action? SelectionChanged;

        /// <summary>
        /// Raised when Enter is pressed on a non-empty selection, carrying the selected row's tag.
        /// </summary>
        public event Action<object?>? Activated;

        /// <summary>
        /// Initializes a new instance of the <see cref="SectionTableView"/> class.
        /// </summary>
        /// <param name="headers">Column headers. Cannot be null; may be empty for a headerless list.</param>
        /// <param name="weights">Relative column widths, one per column. Cannot be null.</param>
        /// <param name="showHeader">Whether to draw the header row.</param>
        /// <param name="padLeft">Blank columns kept to the left of the content (in addition to the border). Default is 0.</param>
        /// <param name="padRight">Blank columns kept to the right of the content (in addition to the border). Default is 0.</param>
        /// <param name="bordered">When true, an uncolored single-line box is drawn and content is inset within it.</param>
        /// <exception cref="ArgumentNullException">Thrown when an argument is null.</exception>
        public SectionTableView(string[] headers, int[] weights, bool showHeader = true, int padLeft = 0, int padRight = 0, bool bordered = false)
        {
            _Headers = headers ?? throw new ArgumentNullException(nameof(headers));
            _Weights = weights ?? throw new ArgumentNullException(nameof(weights));
            _ShowHeader = showHeader && headers.Length > 0;
            _PadLeft = Math.Max(0, padLeft);
            _PadRight = Math.Max(0, padRight);
            _Bordered = bordered;
        }

        /// <summary>
        /// The selected row's tag, or null when the list is empty.
        /// </summary>
        public object? SelectedTag
        {
            get { return _Selected >= 0 && _Selected < _Rows.Count ? _Rows[_Selected].Tag : null; }
        }

        /// <summary>
        /// The zero-based selected index, or -1 when the list is empty.
        /// </summary>
        public int SelectedIndex
        {
            get { return _Rows.Count == 0 ? -1 : _Selected; }
        }

        /// <summary>
        /// The number of rows.
        /// </summary>
        public int Count
        {
            get { return _Rows.Count; }
        }

        /// <summary>
        /// Replace the columns. Existing rows are kept; the caller normally follows with
        /// <see cref="SetRows"/>.
        /// </summary>
        /// <param name="headers">Column headers. Cannot be null.</param>
        /// <param name="weights">Relative column widths. Cannot be null.</param>
        /// <param name="showHeader">Whether to draw the header row.</param>
        /// <exception cref="ArgumentNullException">Thrown when an argument is null.</exception>
        public void SetColumns(string[] headers, int[] weights, bool showHeader = true)
        {
            _Headers = headers ?? throw new ArgumentNullException(nameof(headers));
            _Weights = weights ?? throw new ArgumentNullException(nameof(weights));
            _ShowHeader = showHeader && headers.Length > 0;
        }

        /// <summary>
        /// Set the optional title and subtitle drawn above the column header. Either may be null to
        /// omit that line.
        /// </summary>
        /// <param name="title">The bold title line, or null.</param>
        /// <param name="subtitle">The dim subtitle (for example key hints), or null.</param>
        public void SetHeadings(string? title, string? subtitle)
        {
            _Title = title;
            _Subtitle = subtitle;
        }

        /// <summary>
        /// Replace all rows, clamping the selection and firing <see cref="SelectionChanged"/>.
        /// </summary>
        /// <param name="rows">The rows. Cannot be null.</param>
        /// <param name="emptyMessage">The message shown when there are no rows. Cannot be null.</param>
        /// <exception cref="ArgumentNullException">Thrown when an argument is null.</exception>
        public void SetRows(IEnumerable<TableRow> rows, string emptyMessage)
        {
            if (rows == null)
                throw new ArgumentNullException(nameof(rows));

            _EmptyMessage = emptyMessage ?? throw new ArgumentNullException(nameof(emptyMessage));
            _Rows.Clear();
            _Rows.AddRange(rows);
            if (_Selected >= _Rows.Count)
                _Selected = Math.Max(0, _Rows.Count - 1);
            _ScrollTop = 0;
            SelectionChanged?.Invoke();
        }

        /// <inheritdoc/>
        public void OnFocusChanged(bool focused)
        {
            _Focused = focused;
        }

        /// <inheritdoc/>
        public bool HandleKey(KeyEvent key)
        {
            switch (key.Code)
            {
                case KeyCode.Up: return Move(-1);
                case KeyCode.Down: return Move(1);
                case KeyCode.PageUp: return Move(-10);
                case KeyCode.PageDown: return Move(10);
                case KeyCode.Home: return MoveTo(0);
                case KeyCode.End: return MoveTo(_Rows.Count - 1);
                case KeyCode.Enter:
                    if (SelectedTag != null)
                        Activated?.Invoke(SelectedTag);
                    return true;
                default:
                    return false;
            }
        }

        /// <inheritdoc/>
        public Size Measure(Size available)
        {
            return available;
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

            // An uncolored single-line box; content is inset within the border.
            int inset = 0;
            if (_Bordered)
            {
                surface.DrawBox(new Rect(0, 0, width, height), baseStyle, BorderStyle.Line, string.Empty);
                inset = 1;
            }

            int left = inset + _PadLeft;
            int usable = width - (2 * inset) - _PadLeft - _PadRight;
            int floor = height - inset; // exclusive bottom of the content area
            if (usable <= 0 || inset >= floor)
                return;

            int[] widths = ComputeWidths(usable);
            int y = inset;

            if (!String.IsNullOrEmpty(_Title) && y < floor)
            {
                surface.DrawText(left, y, Fit(_Title!, usable), baseStyle.WithForeground(Color.FromPalette(HeaderColor)).WithAttribute(CellAttributes.Bold, true));
                y++;
            }

            if (!String.IsNullOrEmpty(_Subtitle) && y < floor)
            {
                surface.DrawText(left, y, Fit(_Subtitle!, usable), baseStyle.WithForeground(Color.FromPalette(DimColor)));
                y++;

                // Blank line separating the key-hint row from the content beneath it.
                if (y < floor)
                    y++;
            }

            if (_ShowHeader && y < floor)
            {
                CellStyle headerStyle = baseStyle.WithForeground(Color.FromPalette(HeaderColor)).WithAttribute(CellAttributes.Bold, true);
                DrawCells(surface, left, y, _Headers, widths, headerStyle);
                y++;
            }

            int bodyTop = y;
            int visibleRows = floor - bodyTop;
            if (visibleRows <= 0)
                return;

            if (_Rows.Count == 0)
            {
                surface.DrawText(left, bodyTop, Fit(_EmptyMessage, usable), baseStyle.WithForeground(Color.FromPalette(DimColor)).WithAttribute(CellAttributes.Italic, true));
                return;
            }

            ClampScroll(visibleRows);

            for (int i = 0; i < visibleRows; i++)
            {
                int rowIndex = _ScrollTop + i;
                if (rowIndex >= _Rows.Count)
                    break;

                int rowY = bodyTop + i;
                bool selected = rowIndex == _Selected;

                if (selected && _Focused)
                {
                    CellStyle barStyle = baseStyle.WithForeground(Color.FromPalette(SelectedFg)).WithBackground(Color.FromPalette(SelectedBg));
                    surface.Fill(new Rect(left, rowY, usable, 1), Cell.Blank(barStyle));
                    DrawCells(surface, left, rowY, _Rows[rowIndex].Cells, widths, barStyle);
                }
                else if (selected)
                {
                    DrawCells(surface, left, rowY, _Rows[rowIndex].Cells, widths, baseStyle.WithAttribute(CellAttributes.Bold, true));
                }
                else
                {
                    DrawCells(surface, left, rowY, _Rows[rowIndex].Cells, widths, baseStyle);
                }
            }
        }

        private bool Move(int delta)
        {
            return MoveTo(_Selected + delta);
        }

        private bool MoveTo(int index)
        {
            if (_Rows.Count == 0)
                return true;

            int clamped = Math.Clamp(index, 0, _Rows.Count - 1);
            if (clamped == _Selected)
                return true;

            _Selected = clamped;
            SelectionChanged?.Invoke();
            return true;
        }

        private void ClampScroll(int visibleRows)
        {
            if (_Selected < _ScrollTop)
                _ScrollTop = _Selected;
            else if (_Selected >= _ScrollTop + visibleRows)
                _ScrollTop = _Selected - visibleRows + 1;

            int maxTop = Math.Max(0, _Rows.Count - visibleRows);
            if (_ScrollTop > maxTop)
                _ScrollTop = maxTop;
            if (_ScrollTop < 0)
                _ScrollTop = 0;
        }

        private int[] ComputeWidths(int totalWidth)
        {
            int columns = Math.Max(1, _Weights.Length);
            int gaps = columns - 1;
            int available = Math.Max(columns, totalWidth - gaps);

            int weightSum = 0;
            for (int i = 0; i < columns; i++)
                weightSum += i < _Weights.Length ? Math.Max(1, _Weights[i]) : 1;

            int[] widths = new int[columns];
            int used = 0;
            for (int i = 0; i < columns; i++)
            {
                int weight = i < _Weights.Length ? Math.Max(1, _Weights[i]) : 1;
                widths[i] = Math.Max(3, available * weight / weightSum);
                used += widths[i];
            }

            // Hand any rounding remainder (or overflow) to the last column so the row fills the width.
            widths[columns - 1] += available - used;
            if (widths[columns - 1] < 1)
                widths[columns - 1] = 1;
            return widths;
        }

        private static void DrawCells(ISurface surface, int startX, int y, string[] cells, int[] widths, CellStyle style)
        {
            int x = startX;
            for (int i = 0; i < widths.Length; i++)
            {
                string value = i < cells.Length ? cells[i] : String.Empty;
                surface.DrawText(x, y, Fit(value, widths[i]), style);
                x += widths[i] + 1;
            }
        }

        private static string Fit(string value, int width)
        {
            value ??= String.Empty;
            if (width <= 0)
                return String.Empty;
            if (value.Length == width)
                return value;
            if (value.Length < width)
                return value.PadRight(width);
            if (width == 1)
                return value.Substring(0, 1);
            return value.Substring(0, width - 1) + "…";
        }
    }
}
