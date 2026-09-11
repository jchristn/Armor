namespace Armor.Tui.Widgets
{
    using System;
    using TUIKit;
    using TUIKit.Input;
    using TUIKit.Widgets;

    /// <summary>
    /// The top banner: the product's ASCII-art wordmark on the left and, to its right, the tagline on
    /// the middle row followed by the project link. No border or background is drawn — only the
    /// colored wordmark and text over the terminal's own background. The project link is clickable: a
    /// left click on it opens the URL in the browser.
    /// </summary>
    public sealed class HeaderBanner : IWidget, IMouseAware
    {
        private const byte LogoColor = 6;     // cyan
        private const byte TaglineColor = 7;  // light gray
        private const byte LinkColor = 6;     // cyan

        private readonly string[] _LogoRows;
        private readonly int _LogoWidth;
        private readonly string _Tagline;
        private readonly string _Link;

        // The on-screen cell rectangle the link text occupied on the last render, so a click can be mapped
        // back to it. Row -1 means the link was not drawn (too narrow) and so is not hittable.
        private int _LinkRow = -1;
        private int _LinkStartX;
        private int _LinkWidth;

        /// <summary>
        /// Initializes a new instance of the <see cref="HeaderBanner"/> class.
        /// </summary>
        /// <param name="logoRows">The pre-rendered ASCII-art wordmark rows. Cannot be null.</param>
        /// <param name="tagline">The tagline shown to the right of the wordmark.</param>
        /// <param name="link">The project link shown beneath the tagline.</param>
        /// <exception cref="ArgumentNullException">Thrown when <paramref name="logoRows"/> is null.</exception>
        public HeaderBanner(string[] logoRows, string tagline, string link)
        {
            _LogoRows = logoRows ?? throw new ArgumentNullException(nameof(logoRows));
            _Tagline = tagline ?? string.Empty;
            _Link = link ?? string.Empty;

            int width = 0;
            foreach (string row in _LogoRows)
                width = Math.Max(width, row.Length);
            _LogoWidth = width;
        }

        /// <summary>
        /// The number of rows the wordmark occupies.
        /// </summary>
        public int LogoRowCount
        {
            get { return _LogoRows.Length; }
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
            if (width < 2 || height < 2)
                return;

            CellStyle baseStyle = CellStyle.Default;
            surface.Fill(new Rect(0, 0, width, height), Cell.Blank(baseStyle));

            // Nothing hittable until the link is laid out below; a narrow banner leaves it unset.
            _LinkRow = -1;

            CellStyle logoStyle = baseStyle.WithForeground(Color.FromPalette(LogoColor)).WithAttribute(CellAttributes.Bold, true);
            for (int i = 0; i < _LogoRows.Length; i++)
            {
                if (i >= height)
                    break;
                surface.DrawText(0, i, Clip(_LogoRows[i], width), logoStyle);
            }

            int textX = _LogoWidth + 2;
            int available = width - textX;
            if (available <= 0)
                return;

            int middle = (height - 1) / 2;
            surface.DrawText(textX, middle, Clip(_Tagline, available), baseStyle.WithForeground(Color.FromPalette(TaglineColor)));
            if (middle + 1 < height && _Link.Length > 0)
            {
                string linkText = Clip(_Link, available);
                surface.DrawText(textX, middle + 1, linkText, baseStyle.WithForeground(Color.FromPalette(LinkColor)).WithAttribute(CellAttributes.Underline, true));
                _LinkRow = middle + 1;
                _LinkStartX = textX;
                _LinkWidth = linkText.Length;
            }
        }

        /// <inheritdoc/>
        public bool HandleMouse(MouseEvent mouse)
        {
            if (mouse == null)
                throw new ArgumentNullException(nameof(mouse));

            // A left click on the drawn link text opens the project URL in the browser.
            if (mouse.Kind == MouseEventKind.Press && mouse.Button == MouseButton.Left
                && _LinkRow >= 0 && mouse.Y == _LinkRow
                && mouse.X >= _LinkStartX && mouse.X < _LinkStartX + _LinkWidth)
            {
                return UrlLauncher.TryOpen(_Link);
            }

            return false;
        }

        private static string Clip(string value, int width)
        {
            value ??= string.Empty;
            if (width <= 0)
                return string.Empty;
            if (value.Length <= width)
                return value;
            if (width == 1)
                return value.Substring(0, 1);
            return value.Substring(0, width - 1) + "…";
        }
    }
}
