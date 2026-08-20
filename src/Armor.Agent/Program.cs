namespace Armor.Agent
{
    using Avalonia;
    using Avalonia.Controls;

    /// <summary>
    /// Entry point for the Armor agent. Starts an Avalonia application whose only surface is the
    /// system-tray icon; there is no main window, so the process keeps running until the tray's Exit
    /// action shuts it down.
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
            return BuildAvaloniaApp().StartWithClassicDesktopLifetime(args, ShutdownMode.OnExplicitShutdown);
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
