namespace Armor.Tui
{
    using System;
    using System.Threading.Tasks;
    using Armor.Core.Configuration;
    using Armor.Core.Diagnostics;
    using Armor.Core.Service;
    using TUIKit.Hosting;

    /// <summary>
    /// Entry point for the Armor TUI. It starts logging, opens the shared runtime context, and runs the
    /// menu-driven terminal application until the user quits. Any failure — at startup or in the UI loop —
    /// is written to a crash report under the crash-logs directory and surfaced on the console after the
    /// terminal has been restored, so the app never dies silently.
    /// </summary>
    public static class Program
    {
        /// <summary>
        /// Program entry point.
        /// </summary>
        /// <param name="args">
        /// Command-line arguments. Pass <c>--no-splash</c> to skip the startup splash screen.
        /// </param>
        /// <returns>Zero on normal exit; non-zero on a startup or run failure.</returns>
        public static async Task<int> Main(string[] args)
        {
            bool showSplash = !HasFlag(args, "--no-splash");

            // Start logging before anything else so even a startup failure is captured. The console is
            // disabled here because the TUI owns the screen.
            ArmorPaths paths = new ArmorPaths();
            try
            {
                paths.EnsureDirectories();
            }
            catch (Exception)
            {
                // Directory creation is retried by the context; do not block logging setup on it.
            }
            ArmorLog.Initialize(paths.LogDirectory, paths.CrashLogDirectory, "Armor.Tui", false);
            RegisterGlobalHandlers("Armor.Tui");
            ArmorLog.Info("Armor TUI starting.");

            ArmorContext context;
            try
            {
                Console.WriteLine("Preparing Armor database…");
                context = await ArmorContext.CreateAsync(paths, default, message => Console.WriteLine("  " + message)).ConfigureAwait(false);
                await new StartupMaintenance(context).ReconcileInterruptedBackupsAsync().ConfigureAwait(false);

                // Start the scheduler agent if it is not already running, so schedules fire while the
                // dashboard is open (and it keeps running in the tray afterward). Best-effort — never blocks
                // or fails TUI startup.
                AgentLauncher.EnsureRunning(paths);
            }
            catch (Exception ex)
            {
                string? report = ArmorLog.WriteCrash(ex, "starting the runtime context");
                Console.Error.WriteLine("Armor failed to start: " + ex.Message);
                if (report != null)
                    Console.Error.WriteLine("A crash report was written to: " + report);
                return 1;
            }

            try
            {
                TuiController controller = new TuiController(context, showSplash);
                await TuiApp.RunAsync(controller.Configure).ConfigureAwait(false);
                ArmorLog.Info("Armor TUI exited normally.");
                return 0;
            }
            catch (Exception ex)
            {
                // TuiApp restores the terminal as it unwinds, so this message is visible to the user.
                string? report = ArmorLog.WriteCrash(ex, "running the terminal UI");
                Console.Error.WriteLine();
                Console.Error.WriteLine("Armor stopped unexpectedly: " + ex.Message);
                if (report != null)
                    Console.Error.WriteLine("A crash report was written to: " + report);
                return 2;
            }
            finally
            {
                context.Dispose();
                ArmorLog.Flush();
                ArmorLog.Dispose();
            }
        }

        private static void RegisterGlobalHandlers(string appName)
        {
            AppDomain.CurrentDomain.UnhandledException += (sender, e) =>
            {
                if (e.ExceptionObject is Exception ex)
                    ArmorLog.WriteCrash(ex, "unhandled exception in " + appName);
            };

            TaskScheduler.UnobservedTaskException += (sender, e) =>
            {
                ArmorLog.WriteCrash(e.Exception, "unobserved background task in " + appName);
                e.SetObserved();
            };
        }

        private static bool HasFlag(string[] args, string flag)
        {
            if (args == null)
                return false;
            for (int i = 0; i < args.Length; i++)
            {
                if (String.Equals(args[i], flag, StringComparison.OrdinalIgnoreCase))
                    return true;
            }
            return false;
        }
    }
}
