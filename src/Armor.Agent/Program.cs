namespace Armor.Agent
{
    using System;
    using System.Threading.Tasks;
    using Armor.Core.Configuration;
    using Armor.Core.Diagnostics;
    using Armor.Core.Scheduling;
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

            // Single-instance guard: only one agent may own the scheduler at a time. A second launch — for
            // example the TUI starting an agent when one is already running — exits immediately instead of
            // putting a duplicate tray icon up and double-ticking the scheduler.
            RunLockHandle? instance = AgentInstanceLock.TryAcquire(paths.StateDirectory);
            if (instance == null)
            {
                ArmorLog.Info("Another Armor agent is already running; exiting.");
                ArmorLog.Flush();
                ArmorLog.Dispose();
                return 0;
            }

            ArmorLog.Info("Armor agent starting.");

            try
            {
                using (instance)
                {
                    return BuildAvaloniaApp().StartWithClassicDesktopLifetime(args, ShutdownMode.OnExplicitShutdown);
                }
            }
            catch (Exception ex)
            {
                ArmorLog.WriteCrash(ex, "running the agent");
                string? library = MissingNativeLibrary(ex);
                if (library != null && OperatingSystem.IsLinux())
                {
                    // Building never loads the X11 client libraries, so a minimal install or a stock WSL
                    // distro (WSLg supplies only the display) only finds out here. Say how to fix it.
                    Console.Error.WriteLine("Armor.Agent cannot start: " + library);
                    Console.Error.WriteLine("Install the X11 client libraries Avalonia needs:");
                    Console.Error.WriteLine("  Debian/Ubuntu: sudo apt install libx11-6 libice6 libsm6 libxext6 libxrandr2 libxi6 libxcursor1 libxfixes3 libfontconfig1");
                    Console.Error.WriteLine("  Fedora:        sudo dnf install libX11 libICE libSM libXext libXrandr libXi libXcursor libXfixes fontconfig");
                    return 1;
                }
                return 2;
            }
            finally
            {
                ArmorLog.Info("Armor agent exiting.");
                ArmorLog.Flush();
                ArmorLog.Dispose();
            }
        }

        /// <summary>
        /// Find a missing native library anywhere in an exception chain (Avalonia wraps the loader's
        /// failure in type-initializer and aggregate exceptions).
        /// </summary>
        /// <param name="ex">The exception to inspect.</param>
        /// <returns>The loader's message when a <see cref="DllNotFoundException"/> is in the chain; otherwise null.</returns>
        private static string? MissingNativeLibrary(Exception ex)
        {
            for (Exception? current = ex; current != null; current = current.InnerException)
            {
                if (current is DllNotFoundException)
                    return current.Message;

                if (current is AggregateException aggregate)
                {
                    foreach (Exception inner in aggregate.InnerExceptions)
                    {
                        string? found = MissingNativeLibrary(inner);
                        if (found != null)
                            return found;
                    }
                }
            }

            return null;
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
            // On macOS the agent lives only in the menu bar: without this, every GUI process gets a Dock tile,
            // and an unbundled executable shows there as a generic blank icon. Ignored on other platforms.
            return AppBuilder.Configure<App>()
                .UsePlatformDetect()
                .With(new MacOSPlatformOptions { ShowInDock = false })
                .LogToTrace();
        }
    }
}
