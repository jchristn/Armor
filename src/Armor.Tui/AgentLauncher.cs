namespace Armor.Tui
{
    using System;
    using System.Diagnostics;
    using System.IO;
    using Armor.Core.Configuration;
    using Armor.Core.Diagnostics;
    using Armor.Core.Scheduling;

    /// <summary>
    /// Starts the Armor scheduler agent from the TUI when one is not already running, so schedules run
    /// while the dashboard is open. Detection uses the agent's cross-process single-instance lock, and the
    /// agent's own guard makes the start idempotent — if a race starts two, the second exits at once. The
    /// launcher never throws into TUI startup: a missing executable or a failed start is logged and ignored.
    /// </summary>
    public static class AgentLauncher
    {
        /// <summary>
        /// Start the agent if it is not already running.
        /// </summary>
        /// <param name="paths">Armor's resolved paths (for the state directory and executable discovery). Cannot be null.</param>
        /// <exception cref="ArgumentNullException">Thrown when <paramref name="paths"/> is null.</exception>
        public static void EnsureRunning(ArmorPaths paths)
        {
            if (paths == null)
                throw new ArgumentNullException(nameof(paths));

            try
            {
                if (AgentInstanceLock.IsRunning(paths.StateDirectory))
                    return;

                string? executable = FindAgentExecutable();
                if (executable == null)
                {
                    ArmorLog.Info("Scheduler agent executable not found next to the TUI; not auto-starting it. Run Armor.Agent to enable scheduled backups.");
                    return;
                }

                ProcessStartInfo start = new ProcessStartInfo
                {
                    FileName = executable,
                    WorkingDirectory = Path.GetDirectoryName(executable) ?? String.Empty,
                    UseShellExecute = true, // launch the tray app as an independent process, not a child pipe
                };
                Process.Start(start);
                ArmorLog.Info("Started the Armor scheduler agent for scheduled backups: " + executable);
            }
            catch (Exception ex)
            {
                ArmorLog.Warn("Could not auto-start the Armor scheduler agent: " + ex.Message);
            }
        }

        /// <summary>
        /// Locate the agent executable: first beside the TUI (the installed/published layout where both ship
        /// together), then the sibling project output used during development.
        /// </summary>
        /// <returns>The full path to the agent executable, or null if it cannot be found.</returns>
        private static string? FindAgentExecutable()
        {
            string exeName = OperatingSystem.IsWindows() ? "Armor.Agent.exe" : "Armor.Agent";
            string baseDir = AppContext.BaseDirectory;

            string beside = Path.Combine(baseDir, exeName);
            if (File.Exists(beside))
                return beside;

            // Development layout: .../src/Armor.Tui/bin/<config>/<tfm>/ has a sibling
            // .../src/Armor.Agent/bin/<config>/<tfm>/ with the same configuration and target framework.
            string trimmed = baseDir.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
            string tuiSegment = Path.DirectorySeparatorChar + "Armor.Tui" + Path.DirectorySeparatorChar;
            string agentSegment = Path.DirectorySeparatorChar + "Armor.Agent" + Path.DirectorySeparatorChar;
            int index = trimmed.LastIndexOf(tuiSegment, StringComparison.OrdinalIgnoreCase);
            if (index >= 0)
            {
                string siblingDir = trimmed.Substring(0, index) + agentSegment + trimmed.Substring(index + tuiSegment.Length);
                string sibling = Path.Combine(siblingDir, exeName);
                if (File.Exists(sibling))
                    return sibling;
            }

            return null;
        }
    }
}
