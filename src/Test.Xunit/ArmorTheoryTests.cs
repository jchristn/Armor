namespace Test.Xunit
{
    using System.Threading;
    using System.Threading.Tasks;
    using Test.Shared;
    using Touchstone.Core;
    using Xunit;
    using global::Xunit.Abstractions;

    /// <summary>
    /// Runs each non-skipped Armor descriptor as an individual theory row for per-test visibility.
    /// </summary>
    public sealed class ArmorTheoryTests
    {
        private readonly ITestOutputHelper _Output;

        /// <summary>
        /// Initializes a new instance of the <see cref="ArmorTheoryTests"/> class.
        /// </summary>
        /// <param name="output">xUnit output helper used to log the running case name.</param>
        public ArmorTheoryTests(ITestOutputHelper output)
        {
            _Output = output;
        }

        /// <summary>
        /// Enumerates every non-skipped descriptor as a theory row.
        /// </summary>
        /// <returns>Theory data containing one entry per runnable case.</returns>
        public static TheoryData<TestCaseDescriptor> TestCases()
        {
            TheoryData<TestCaseDescriptor> data = new TheoryData<TestCaseDescriptor>();

            foreach (TestSuiteDescriptor suite in ArmorSuites.All)
            {
                foreach (TestCaseDescriptor testCase in suite.Cases)
                {
                    if (!testCase.Skip)
                        data.Add(testCase);
                }
            }

            return data;
        }

        /// <summary>
        /// Execute a single descriptor.
        /// </summary>
        /// <param name="testCase">The descriptor to execute.</param>
        /// <returns>A task that completes when the case has run.</returns>
        [Theory]
        [MemberData(nameof(TestCases))]
        public async Task RunTest(TestCaseDescriptor testCase)
        {
            _Output.WriteLine("Running: " + testCase.DisplayName);
            await testCase.ExecuteAsync(CancellationToken.None);
        }
    }
}
