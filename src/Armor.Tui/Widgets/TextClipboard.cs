namespace Armor.Tui.Widgets
{
    using System;
    using System.Diagnostics;
    using System.Runtime.InteropServices;

    /// <summary>
    /// Writes text to the host clipboard by piping it to the platform clipboard tool — <c>clip</c> on
    /// Windows, <c>pbcopy</c> on macOS, and <c>wl-copy</c>/<c>xclip</c>/<c>xsel</c> on Linux. This is used
    /// instead of an OSC 52 terminal escape because it does not interleave with the TUI renderer's own
    /// writes to stdout and does not depend on the terminal advertising OSC 52 support. Every failure is
    /// swallowed and reported as <c>false</c>.
    /// </summary>
    internal static class TextClipboard
    {
        /// <summary>
        /// Copy text to the system clipboard. Returns false when no clipboard tool is available or the
        /// write fails.
        /// </summary>
        /// <param name="text">The text to place on the clipboard.</param>
        /// <returns>True on success; false otherwise.</returns>
        public static bool TrySetText(string text)
        {
            string payload = text ?? String.Empty;

            if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
                return Pipe("clip", null, payload);

            if (RuntimeInformation.IsOSPlatform(OSPlatform.OSX))
                return Pipe("pbcopy", null, payload);

            // Linux/BSD: try Wayland first, then the two common X11 tools.
            return Pipe("wl-copy", null, payload)
                || Pipe("xclip", "-selection clipboard", payload)
                || Pipe("xsel", "--clipboard --input", payload);
        }

        private static bool Pipe(string exe, string? arguments, string payload)
        {
            try
            {
                ProcessStartInfo psi = new ProcessStartInfo(exe)
                {
                    UseShellExecute = false,
                    CreateNoWindow = true,
                    RedirectStandardInput = true,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                };
                if (!String.IsNullOrEmpty(arguments))
                    psi.Arguments = arguments;

                Process? process = Process.Start(psi);
                if (process == null)
                    return false;

                using (process)
                {
                    process.StandardInput.Write(payload);
                    process.StandardInput.Close();
                    // These tools exit promptly once stdin closes; bound the wait so a misbehaving tool
                    // cannot hang the UI thread.
                    if (!process.WaitForExit(2000))
                        return false;
                    return process.ExitCode == 0;
                }
            }
            catch (Exception)
            {
                // Missing tool, no permission, no display — treat as "clipboard unavailable".
                return false;
            }
        }
    }
}
