namespace Test.Nunit
{
    using System.Collections.Generic;
    using System.Threading.Tasks;
    using NUnit.Framework;
    using Test.Shared;
    using Touchstone.Core;
    using Touchstone.NunitAdapter;

    /// <summary>
    /// Runs every Armor descriptor sequentially through the Touchstone executor in a single test.
    /// </summary>
    [TestFixture]
    public sealed class ArmorNunitFactTests : TouchstoneNunitBase
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
        [Test]
        public async Task RunAll()
        {
            await RunAllAsync().ConfigureAwait(false);
        }
    }
}
