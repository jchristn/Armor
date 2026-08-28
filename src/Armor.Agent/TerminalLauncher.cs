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
            string? executablePath = ResolveTuiPath();
            if (executablePath == null)
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

        /// <summary>
        /// Locate the TUI executable: first beside the agent (the installed layout where both ship
        /// together), then the sibling project output used during development. Returns null when neither
        /// exists.
        /// </summary>
        private static string? ResolveTuiPath()
        {
            string executableName = OperatingSystem.IsWindows() ? "Armor.Tui.exe" : "Armor.Tui";
            string baseDir = AppContext.BaseDirectory;

            string beside = Path.Combine(baseDir, executableName);
            if (File.Exists(beside))
                return beside;

            // Development layout: .../src/Armor.Agent/bin/<config>/<tfm>/ has a sibling
            // .../src/Armor.Tui/bin/<config>/<tfm>/ with the same configuration and target framework.
            string trimmed = baseDir.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
            string agentSegment = Path.DirectorySeparatorChar + "Armor.Agent" + Path.DirectorySeparatorChar;
            string tuiSegment = Path.DirectorySeparatorChar + "Armor.Tui" + Path.DirectorySeparatorChar;
            int index = trimmed.LastIndexOf(agentSegment, StringComparison.OrdinalIgnoreCase);
            if (index >= 0)
            {
                string siblingDir = trimmed.Substring(0, index) + tuiSegment + trimmed.Substring(index + agentSegment.Length);
                string sibling = Path.Combine(siblingDir, executableName);
                if (File.Exists(sibling))
                    return sibling;
            }

            return null;
        }
    }
}
