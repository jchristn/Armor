namespace Armor.Core.Diagnostics
{
    using System;
    using System.Diagnostics;
    using System.Runtime.InteropServices;
    using System.Text;
    using System.Threading.Tasks;

    /// <summary>
    /// Raises a native desktop (toast/balloon) notification on the current operating system. It shells out
    /// to the platform's built-in facility — a Windows tray balloon via PowerShell, <c>osascript</c> on
    /// macOS, and <c>notify-send</c> on Linux — so no extra dependency is required. Delivery is best-effort
    /// and fire-and-forget: every call returns immediately and a failure (missing tool, no desktop session)
    /// is swallowed, so a notification can never disrupt or slow the backup that triggered it.
    /// </summary>
    public static class DesktopNotifier
    {
        /// <summary>
        /// Post a desktop notification. Returns immediately; the notification is delivered on a background
        /// thread and any failure is silently ignored.
        /// </summary>
        /// <param name="title">The notification title. Null is treated as empty.</param>
        /// <param name="message">The notification body. Null is treated as empty.</param>
        public static void Notify(string title, string message)
        {
            string safeTitle = (title ?? String.Empty).Trim();
            string safeMessage = (message ?? String.Empty).Trim();
            if (safeTitle.Length == 0 && safeMessage.Length == 0)
                return;

            _ = Task.Run(() => Send(safeTitle, safeMessage));
        }

        private static void Send(string title, string message)
        {
            try
            {
                if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
                    NotifyWindows(title, message);
                else if (RuntimeInformation.IsOSPlatform(OSPlatform.OSX))
                    NotifyMac(title, message);
                else if (RuntimeInformation.IsOSPlatform(OSPlatform.Linux))
                    NotifyLinux(title, message);
            }
            catch (Exception)
            {
                // A desktop notification is a courtesy; a failure here must never surface to the caller.
            }
        }

        private static void NotifyWindows(string title, string message)
        {
            // Show a tray balloon from a throwaway NotifyIcon. This needs no external module (unlike a
            // WinRT toast, which requires a registered AppUserModelID) and works on Windows 10 and 11. The
            // script is passed base64-encoded via -EncodedCommand so titles/messages need no shell quoting.
            string script =
                "Add-Type -AssemblyName System.Windows.Forms;" +
                "Add-Type -AssemblyName System.Drawing;" +
                "$n = New-Object System.Windows.Forms.NotifyIcon;" +
                "$n.Icon = [System.Drawing.SystemIcons]::Information;" +
                "$n.BalloonTipTitle = '" + PowerShellLiteral(title) + "';" +
                "$n.BalloonTipText = '" + PowerShellLiteral(message) + "';" +
                "$n.Visible = $true;" +
                "$n.ShowBalloonTip(10000);" +
                "Start-Sleep -Seconds 6;" +
                "$n.Dispose();";
            string encoded = Convert.ToBase64String(Encoding.Unicode.GetBytes(script));

            ProcessStartInfo psi = new ProcessStartInfo("powershell.exe");
            psi.ArgumentList.Add("-NoProfile");
            psi.ArgumentList.Add("-NonInteractive");
            psi.ArgumentList.Add("-WindowStyle");
            psi.ArgumentList.Add("Hidden");
            psi.ArgumentList.Add("-EncodedCommand");
            psi.ArgumentList.Add(encoded);
            Launch(psi);
        }

        private static void NotifyMac(string title, string message)
        {
            ProcessStartInfo psi = new ProcessStartInfo("osascript");
            psi.ArgumentList.Add("-e");
            psi.ArgumentList.Add("display notification \"" + AppleScriptLiteral(message) + "\" with title \"" + AppleScriptLiteral(title) + "\"");
            Launch(psi);
        }

        private static void NotifyLinux(string title, string message)
        {
            ProcessStartInfo psi = new ProcessStartInfo("notify-send");
            psi.ArgumentList.Add("-a");
            psi.ArgumentList.Add("Armor");
            psi.ArgumentList.Add(title);
            psi.ArgumentList.Add(message);
            Launch(psi);
        }

        private static void Launch(ProcessStartInfo psi)
        {
            // No shell, no window, and capture the child's streams so nothing it prints can leak onto the
            // terminal the TUI is drawing to. We start and detach — the notification outlives this call.
            psi.UseShellExecute = false;
            psi.CreateNoWindow = true;
            psi.RedirectStandardOutput = true;
            psi.RedirectStandardError = true;
            using (Process.Start(psi))
            {
                // Disposing the handle releases our reference; it does not terminate the child process.
            }
        }

        private static string PowerShellLiteral(string value)
        {
            // Inside a single-quoted PowerShell string, only the single quote is special; double it to escape.
            return (value ?? String.Empty).Replace("'", "''");
        }

        private static string AppleScriptLiteral(string value)
        {
            return (value ?? String.Empty).Replace("\\", "\\\\").Replace("\"", "\\\"");
        }
    }
}
