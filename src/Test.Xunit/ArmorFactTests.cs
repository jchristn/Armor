namespace Test.Xunit
{
    using System.Collections.Generic;
    using System.Threading.Tasks;
    using Test.Shared;
    using Touchstone.Core;
    using Touchstone.XunitAdapter;
    using Xunit;

    /// <summary>
    /// Runs every Armor descriptor sequentially through the Touchstone executor in a single fact,
    /// honoring suite lifecycle hooks and preserving order.
    /// </summary>
    public sealed class ArmorFactTests : TouchstoneFactBase
    {
        /// <summary>
        /// The full set of Armor suites to execute.
        /// </summary>
        protected override IReadOnlyList<TestSuiteDescriptor> Suites
        {
            get { return ArmorSuites.All; }
        }

        /// <summary>
        /// Execute all suites.
        /// </summary>
        /// <returns>A task that completes when every suite has run.</returns>
        [Fact]
        public async Task RunAll()
        {
            await RunAllAsync();
        }
    }
}
