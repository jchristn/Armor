namespace Test.Shared
{
    using System.Collections.Generic;
    using Touchstone.Core;

    /// <summary>
    /// Aggregates every Armor test suite so that any runner (console, xUnit, NUnit) can consume
    /// the entire descriptor set through a single property.
    /// </summary>
    public static class ArmorSuites
    {
        /// <summary>
        /// Every suite Armor exposes for execution. Runners iterate this collection to run all
        /// positive and negative cases across the engine.
        /// </summary>
        public static IReadOnlyList<TestSuiteDescriptor> All
        {
            get
            {
                return new List<TestSuiteDescriptor>
                {
                    IdentifierSuite.Build(),
                    ConfigurationSuite.Build(),
                    DatabaseSuite.Build(),
                    CryptoSuite.Build(),
                    ChunkStoreSuite.Build(),
                    StorageSuite.Build(),
                    EngineSuite.Build(),
                    SchedulingSuite.Build(),
                    RetentionSuite.Build(),
                    SelfBackupSuite.Build(),
                    ServiceSuite.Build(),
                    CoverageSuite.Build()
                };
            }
        }
    }
}
