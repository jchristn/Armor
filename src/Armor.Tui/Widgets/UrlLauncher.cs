namespace Armor.Tui.Widgets
{
    using System;
    using System.Diagnostics;
    using System.Runtime.InteropServices;

    /// <summary>
    /// Opens a URL in the user's default browser through the platform launcher — the shell association on
    /// Windows, <c>open</c> on macOS, and <c>xdg-open</c> on Linux/BSD. Used so a clickable link in the TUI
    /// (the header banner, the startup splash) actually opens when the pointer selects it. Every failure is
    /// swallowed and reported as <c>false</c> so a missing launcher can never disturb the terminal UI.
    /// </summary>
    internal static class UrlLauncher
    {
        /// <summary>
        /// Open the supplied URL in the default browser. Returns false when the URL is empty, is not an
        /// absolute http/https URL, or no launcher is available.
        /// </summary>
        /// <param name="url">The URL to open.</param>
        /// <returns>True when a launcher was started; false otherwise.</returns>
        public static bool TryOpen(string? url)
        {
            if (String.IsNullOrWhiteSpace(url))
                return false;

            // Only follow absolute http/https links. This keeps a stray click from launching a file:// or
            // custom-scheme handler off text that merely looks like a link.
            if (!Uri.TryCreate(url, UriKind.Absolute, out Uri? parsed)
                || (parsed.Scheme != Uri.UriSchemeHttp && parsed.Scheme != Uri.UriSchemeHttps))
                return false;

            try
            {
                if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
                {
                    // UseShellExecute lets the OS resolve the default browser; the URL is passed as the
                    // file name so the shell association handles it.
                    return Start(new ProcessStartInfo(parsed.AbsoluteUri) { UseShellExecute = true });
                }

                if (RuntimeInformation.IsOSPlatform(OSPlatform.OSX))
                    return Start(new ProcessStartInfo("open", QuoteArg(parsed.AbsoluteUri)) { UseShellExecute = false, CreateNoWindow = true });

                return Start(new ProcessStartInfo("xdg-open", QuoteArg(parsed.AbsoluteUri)) { UseShellExecute = false, CreateNoWindow = true });
            }
            catch (Exception)
            {
                // No browser, no launcher, no display, or a blocked association — treat as "cannot open".
                return false;
            }
        }

        private static bool Start(ProcessStartInfo psi)
        {
            Process? process = Process.Start(psi);
            if (process == null)
                return false;
            process.Dispose();
            return true;
        }

        private static string QuoteArg(string value)
        {
            return "\"" + value.Replace("\"", "\\\"") + "\"";
        }
    }
}
