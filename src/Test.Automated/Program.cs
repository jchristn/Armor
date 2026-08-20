namespace Test.Automated
{
    using System.Threading.Tasks;
    using Test.Shared;
    using Touchstone.Cli;

    /// <summary>
    /// Console entry point that executes every Armor test suite through the Touchstone console runner.
    /// </summary>
    public static class Program
    {
        /// <summary>
        /// Program entry point.
        /// </summary>
        /// <param name="args">Command-line arguments. Use <c>--results &lt;path&gt;</c> to export JSON results.</param>
        /// <returns>Zero if all tests pass; non-zero if any test fails.</returns>
        public static async Task<int> Main(string[] args)
        {
            string? resultsPath = null;

            for (int i = 0; i < args.Length; i++)
            {
                if (args[i] == "--results" && i + 1 < args.Length)
                {
                    resultsPath = args[i + 1];
                    break;
                }
            }

            return await ConsoleRunner.RunAsync(
                ArmorSuites.All,
                resultsPath: resultsPath).ConfigureAwait(false);
        }
    }
}
