namespace Armor.Agent
{
    using System;
    using System.Diagnostics;
    using System.IO;

    /// <summary>
    /// Launches the Armor TUI in a platform-appropriate terminal window. The TUI executable is expected
    /// to sit alongside the agent in the same installation directory.
    /// </summary>
    public static class TerminalLauncher
    {
        /// <summary>
        /// Launch the TUI. Does nothing if the executable cannot be located or the launch fails.
        /// </summary>
        public static void LaunchTui()
        {
            string executablePath = ResolveTuiPath();
            if (!File.Exists(executablePath))
                return;

            try
            {
                if (OperatingSystem.IsWindows())
                {
                    ProcessStartInfo info = new ProcessStartInfo(executablePath);
                    info.UseShellExecute = true;
                    Process.Start(info);
                }
                else if (OperatingSystem.IsMacOS())
                {
                    Process.Start("open", "-a Terminal \"" + executablePath + "\"");
                }
                else
                {
                    ProcessStartInfo info = new ProcessStartInfo();
                    info.FileName = "x-terminal-emulator";
                    info.Arguments = "-e \"" + executablePath + "\"";
                    info.UseShellExecute = false;
                    Process.Start(info);
                }
            }
            catch (Exception)
            {
            }
        }

        private static string ResolveTuiPath()
        {
            string executableName = OperatingSystem.IsWindows() ? "Armor.Tui.exe" : "Armor.Tui";
            return Path.Combine(AppContext.BaseDirectory, executableName);
        }
    }
}
