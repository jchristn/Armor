namespace Armor.Agent
{
    using System;
    using System.Threading.Tasks;
    using Armor.Core.Configuration;
    using Armor.Core.Diagnostics;
    using Avalonia;
    using Avalonia.Controls;

    /// <summary>
    /// Entry point for the Armor agent. Starts an Avalonia application whose only surface is the
    /// system-tray icon; there is no main window, so the process keeps running until the tray's Exit
    /// action shuts it down. Logging and crash reporting are started first so a background scheduler or
    /// UI failure is captured rather than lost.
    /// </summary>
    public static class Program
    {
        /// <summary>
        /// Program entry point.
        /// </summary>
        /// <param name="args">Command-line arguments passed to the Avalonia lifetime.</param>
        /// <returns>The process exit code.</returns>
        public static int Main(string[] args)
        {
            ArmorPaths paths = new ArmorPaths();
            try
            {
                paths.EnsureDirectories();
            }
            catch (Exception)
            {
                // Directory creation is retried when the context opens.
            }
            ArmorLog.Initialize(paths.LogDirectory, paths.CrashLogDirectory, "Armor.Agent", false);
            RegisterGlobalHandlers();
            ArmorLog.Info("Armor agent starting.");

            try
            {
                return BuildAvaloniaApp().StartWithClassicDesktopLifetime(args, ShutdownMode.OnExplicitShutdown);
            }
            catch (Exception ex)
            {
                ArmorLog.WriteCrash(ex, "running the agent");
                return 2;
            }
            finally
            {
                ArmorLog.Info("Armor agent exiting.");
                ArmorLog.Flush();
                ArmorLog.Dispose();
            }
        }

        private static void RegisterGlobalHandlers()
        {
            AppDomain.CurrentDomain.UnhandledException += (sender, e) =>
            {
                if (e.ExceptionObject is Exception ex)
                    ArmorLog.WriteCrash(ex, "unhandled exception in Armor.Agent");
            };

            TaskScheduler.UnobservedTaskException += (sender, e) =>
            {
                ArmorLog.WriteCrash(e.Exception, "unobserved background task in Armor.Agent");
                e.SetObserved();
            };
        }

        /// <summary>
        /// Build the Avalonia application.
        /// </summary>
        /// <returns>The configured application builder.</returns>
        public static AppBuilder BuildAvaloniaApp()
        {
            return AppBuilder.Configure<App>()
                .UsePlatformDetect()
                .LogToTrace();
        }
    }
}
