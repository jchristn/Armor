namespace Armor.Tui
{
    using System;
    using System.Threading.Tasks;
    using Armor.Core.Service;
    using TUIKit.Hosting;

    /// <summary>
    /// Entry point for the Armor TUI. It opens the shared runtime context and runs the menu-driven
    /// terminal application until the user quits.
    /// </summary>
    public static class Program
    {
        /// <summary>
        /// Program entry point.
        /// </summary>
        /// <param name="args">
        /// Command-line arguments. Pass <c>--no-splash</c> to skip the startup splash screen.
        /// </param>
        /// <returns>Zero on normal exit; non-zero if the context could not be initialized.</returns>
        public static async Task<int> Main(string[] args)
        {
            bool showSplash = !HasFlag(args, "--no-splash");

            ArmorContext context;
            try
            {
                context = await ArmorContext.CreateAsync().ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine("Armor failed to start: " + ex.Message);
                return 1;
            }

            try
            {
                TuiController controller = new TuiController(context, showSplash);
                await TuiApp.RunAsync(controller.Configure).ConfigureAwait(false);
                return 0;
            }
            finally
            {
                context.Dispose();
            }
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
