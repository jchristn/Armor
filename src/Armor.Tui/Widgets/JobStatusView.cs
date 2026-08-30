namespace Armor.Tui.Widgets
{
    using System;
    using System.Collections.Generic;
    using TUIKit;
    using TUIKit.Input;
    using TUIKit.Unicode;
    using TUIKit.Widgets;

    /// <summary>
    /// An immutable snapshot of one in-progress backup, handed to the <see cref="JobStatusView"/> for
    /// display. The <see cref="Id"/> is the host's handle for the running job, echoed back when the user
    /// asks to manage it.
    /// </summary>
    public sealed class JobSnapshot
    {
        /// <summary>
        /// Initializes a new instance of the <see cref="JobSnapshot"/> class.
        /// </summary>
        /// <param name="id">The host's opaque job handle. Cannot be null.</param>
        /// <param name="label">The human-readable job label (for example "Full backup to 'USB'"). Cannot be null.</param>
        /// <param name="percent">Completion percent, 0 to 100.</param>
        /// <param name="filesDone">Files processed so far.</param>
        /// <param name="filesTotal">Total files to process, if known.</param>
        /// <param name="bytesDone">Bytes processed so far.</param>
        /// <param name="bytesTotal">Total bytes to process, if known.</param>
        /// <param name="cancelling">True once cancellation has been requested but the job has not yet stopped.</param>
        /// <param name="scanning">True while the run is still pre-scanning the source (before any file is copied).</param>
        /// <param name="external">True when the run is owned by another process (the background agent), so it shows an
        /// indeterminate status line — from <paramref name="note"/> — instead of a live progress bar, and cannot be
        /// canceled from here.</param>
        /// <param name="note">For an external run, the indeterminate status line to display (for example
        /// "Scheduled run in progress — started 19:52").</param>
        public JobSnapshot(string id, string label, int percent, int filesDone, int filesTotal, long bytesDone, long bytesTotal, bool cancelling, bool scanning, bool external = false, string? note = null)
        {
            Id = id ?? throw new ArgumentNullException(nameof(id));
            Label = label ?? String.Empty;
            Percent = Math.Clamp(percent, 0, 100);
            FilesDone = filesDone;
            FilesTotal = filesTotal;
            BytesDone = bytesDone;
            BytesTotal = bytesTotal;
            Cancelling = cancelling;
            Scanning = scanning;
            External = external;
            Note = note ?? String.Empty;
        }

        /// <summary>The host's opaque job handle.</summary>
        public string Id { get; }

        /// <summary>The human-readable job label.</summary>
        public string Label { get; }

        /// <summary>Completion percent, 0 to 100.</summary>
        public int Percent { get; }

        /// <summary>Files processed so far.</summary>
        public int FilesDone { get; }

        /// <summary>Total files to process, if known.</summary>
        public int FilesTotal { get; }

        /// <summary>Bytes processed so far.</summary>
        public long BytesDone { get; }

        /// <summary>Total bytes to process, if known.</summary>
        public long BytesTotal { get; }

        /// <summary>True once cancellation has been requested but the job has not yet stopped.</summary>
        public bool Cancelling { get; }

        /// <summary>True while the run is still pre-scanning the source (before any file is copied).</summary>
        public bool Scanning { get; }

        /// <summary>True when the run is owned by another process (the background agent).</summary>
        public bool External { get; }

        /// <summary>For an external run, the indeterminate status line to display.</summary>
        public string Note { get; }
    }

    /// <summary>
    /// The focusable "status workspace": a live view of in-progress backups. When there are none it shows
    /// a quiet placeholder. When there are one or more, it shows the selected job's progress rectangle,
    /// framed by a blank line above and below. Tab moves focus here; Up/Down/PageUp/PageDown/Home/End
    /// step through the running jobs; Enter raises <see cref="Activated"/> so the host can offer to cancel
    /// the selected job. Keys it does not use fall through so Tab and Escape still traverse the shell.
    /// </summary>
    public sealed class JobStatusView : IWidget, IFocusable, IFocusAware
    {
        private const int BarWidth = 30;
        private const byte TitleColor = 6;
        private const byte DimColor = 8;
        private const byte AccentColor = 2;

        private readonly List<JobSnapshot> _Jobs = new List<JobSnapshot>();
        private int _Selected;
        private bool _Focused;

        /// <summary>
        /// Raised with the selected job's id when the user presses Enter to manage it.
        /// </summary>
        public event Action<string>? Activated;

        /// <summary>
        /// Replace the set of displayed jobs. The selection is preserved by position where possible.
        /// </summary>
        /// <param name="jobs">The current in-progress jobs. Cannot be null.</param>
        /// <exception cref="ArgumentNullException">Thrown when <paramref name="jobs"/> is null.</exception>
        public void SetJobs(IReadOnlyList<JobSnapshot> jobs)
        {
            if (jobs == null)
                throw new ArgumentNullException(nameof(jobs));

            _Jobs.Clear();
            _Jobs.AddRange(jobs);
            if (_Selected >= _Jobs.Count)
                _Selected = Math.Max(0, _Jobs.Count - 1);
            if (_Selected < 0)
                _Selected = 0;
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
            if (_Jobs.Count == 0)
                return false;

            switch (key.Code)
            {
                case KeyCode.Up: Move(-1); return true;
                case KeyCode.Down: Move(1); return true;
                case KeyCode.PageUp: Move(-5); return true;
                case KeyCode.PageDown: Move(5); return true;
                case KeyCode.Home: _Selected = 0; return true;
                case KeyCode.End: _Selected = _Jobs.Count - 1; return true;
                case KeyCode.Enter:
                    if (_Selected >= 0 && _Selected < _Jobs.Count)
                        Activated?.Invoke(_Jobs[_Selected].Id);
                    return true;
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

            if (_Jobs.Count == 0)
            {
                surface.DrawText(1, 0, Clip("No active jobs.", width - 1), baseStyle.WithForeground(Color.FromPalette(DimColor)));
                return;
            }

            if (_Selected < 0 || _Selected >= _Jobs.Count)
                _Selected = 0;
            JobSnapshot job = _Jobs[_Selected];

            // Header: count, position within the list, and (when focused) the navigation hint.
            string header = _Jobs.Count == 1
                ? "Active job"
                : "Active jobs (" + _Jobs.Count + ")   [" + (_Selected + 1) + "/" + _Jobs.Count + "]";
            CellStyle headerStyle = baseStyle.WithForeground(Color.FromPalette(_Focused ? TitleColor : DimColor));
            if (_Focused)
                headerStyle = headerStyle.WithAttribute(CellAttributes.Bold, true);
            surface.DrawText(1, 0, Clip(header, width - 1), headerStyle);

            string hint = _Focused
                ? "↑↓ select · Enter manage · Tab out"
                : "Tab here to manage";
            int hintX = width - 1 - hint.Length;
            if (hintX > header.Length + 3)
                surface.DrawText(hintX, 0, hint, baseStyle.WithForeground(Color.FromPalette(DimColor)));

            // Row 1 stays blank — the linebreak above the progress rectangle.

            // Rows 2-4: the progress rectangle. When focused, the title is reversed so the user can see
            // the status workspace holds focus. During the pre-scan there is no percentage yet, so the
            // bar is replaced by a live "scanning" line whose file count climbs as the source is walked.
            CellStyle titleStyle = baseStyle.WithForeground(Color.FromPalette(AccentColor)).WithAttribute(CellAttributes.Bold, true);
            if (_Focused)
                titleStyle = titleStyle.WithAttribute(CellAttributes.Reverse, true);

            // Agent-owned runs carry an "(agent)" tag so it is clear they are driven by the background
            // process (and are canceled from the tray, not here); they otherwise draw the same bar as a
            // local run, from the live progress the engine flushes to the database.
            string titleText = " " + job.Label + (job.External ? "   (agent)" : "");
            if (2 < height)
                surface.DrawText(1, 2, Clip(titleText, width - 1), titleStyle);

            if (job.Scanning)
            {
                string scan = "Scanning for files… " + job.FilesTotal + " found";
                if (job.BytesTotal > 0)
                    scan += " · " + FormatBytes(job.BytesTotal);
                if (job.Cancelling)
                    scan += "   — cancelling";
                // Aligned with the title (same " " + text at column 1), so there is no extra indent.
                if (3 < height)
                    surface.DrawText(1, 3, Clip(" " + scan, width - 1), baseStyle.WithForeground(Color.FromPalette(AccentColor)));
                // Row 4 intentionally left blank while scanning.
            }
            else
            {
                int filled = Math.Clamp(job.Percent * BarWidth / 100, 0, BarWidth);
                string bar = new string('█', filled) + new string('░', BarWidth - filled);
                string detail = job.FilesDone + " / " + job.FilesTotal + " files · " + FormatBytes(job.BytesDone) + " of " + FormatBytes(job.BytesTotal);
                if (job.Cancelling)
                    detail += "   — cancelling";

                if (3 < height)
                    surface.DrawText(3, 3, Clip(bar + "  " + job.Percent + "%", width - 3), baseStyle);
                if (4 < height)
                    surface.DrawText(3, 4, Clip(detail, width - 3), baseStyle.WithForeground(Color.FromPalette(DimColor)));
            }

            // Row 5 stays blank — the linebreak below the progress rectangle.
        }

        private void Move(int delta)
        {
            _Selected = Math.Clamp(_Selected + delta, 0, _Jobs.Count - 1);
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

        private static string FormatBytes(long bytes)
        {
            if (bytes < 1024)
                return bytes + " B";
            string[] units = { "KB", "MB", "GB", "TB", "PB" };
            double value = bytes;
            int unit = -1;
            while (value >= 1024 && unit < units.Length - 1)
            {
                value /= 1024;
                unit++;
            }
            return value.ToString("0.0") + " " + units[unit];
        }
    }
}
