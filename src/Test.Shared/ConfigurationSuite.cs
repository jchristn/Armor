namespace Test.Shared
{
    using System;
    using System.Collections.Generic;
    using System.IO;
    using System.Threading.Tasks;
    using Armor.Core.Configuration;
    using Armor.Core.Exceptions;
    using Touchstone.Core;

    /// <summary>
    /// Verifies configuration load, save, validation, clamping, and environment overrides.
    /// </summary>
    public static class ConfigurationSuite
    {
        /// <summary>
        /// Build the configuration test suite.
        /// </summary>
        /// <returns>The configuration suite descriptor.</returns>
        public static TestSuiteDescriptor Build()
        {
            return new TestSuiteDescriptor(
                suiteId: "Configuration",
                displayName: "Configuration",
                cases: new List<TestCaseDescriptor>
                {
                    new TestCaseDescriptor(
                        suiteId: "Configuration",
                        caseId: "FirstRunCreatesFileAndDirectories",
                        displayName: "First run writes a default config and creates directories",
                        executeAsync: async ct =>
                        {
                            using (TempWorkspace ws = new TempWorkspace())
                            {
                                ArmorPaths paths = new ArmorPaths(ws.Combine("home"));
                                SettingsManager manager = new SettingsManager(paths, _ => null);

                                ArmorSettings settings = await manager.LoadAsync(ct).ConfigureAwait(false);

                                Check.True(File.Exists(paths.ConfigFilePath), "config file should exist after first load");
                                Check.True(Directory.Exists(paths.LogDirectory), "log directory should exist");
                                Check.True(Directory.Exists(paths.StateDirectory), "state directory should exist");
                                Check.Equal(paths.DefaultDatabasePath, settings.DatabaseFilename, "database filename should default");
                            }
                        }),

                    new TestCaseDescriptor(
                        suiteId: "Configuration",
                        caseId: "SaveReloadRoundTrip",
                        displayName: "Saved values survive a reload",
                        executeAsync: async ct =>
                        {
                            using (TempWorkspace ws = new TempWorkspace())
                            {
                                ArmorPaths paths = new ArmorPaths(ws.Combine("home"));
                                SettingsManager manager = new SettingsManager(paths, _ => null);

                                ArmorSettings settings = await manager.LoadAsync(ct).ConfigureAwait(false);
                                settings.EngineConcurrency = 9;
                                settings.SchedulerTickSeconds = 120;
                                settings.Chunking.MinSizeBytes = 4096;
                                settings.Logging.ConsoleLogging = false;
                                await manager.SaveAsync(settings, ct).ConfigureAwait(false);

                                ArmorSettings reloaded = await manager.LoadAsync(ct).ConfigureAwait(false);
                                Check.Equal(9, reloaded.EngineConcurrency, "engine concurrency persisted");
                                Check.Equal(120, reloaded.SchedulerTickSeconds, "scheduler tick persisted");
                                Check.Equal(4096, reloaded.Chunking.MinSizeBytes, "chunk min persisted");
                                Check.False(reloaded.Logging.ConsoleLogging, "console logging persisted");
                            }
                        }),

                    new TestCaseDescriptor(
                        suiteId: "Configuration",
                        caseId: "MalformedJsonThrows",
                        displayName: "Malformed configuration JSON throws a configuration exception",
                        executeAsync: _ =>
                        {
                            SettingsManager manager = new SettingsManager(new ArmorPaths(Path.GetTempPath()), __ => null);
                            try
                            {
                                manager.Parse("{ this is not valid json ");
                                throw new InvalidOperationException("Expected ArmorConfigurationException.");
                            }
                            catch (ArmorConfigurationException)
                            {
                            }
                            return Task.CompletedTask;
                        }),

                    new TestCaseDescriptor(
                        suiteId: "Configuration",
                        caseId: "NullJsonThrows",
                        displayName: "Null or whitespace JSON throws ArgumentNullException",
                        executeAsync: _ =>
                        {
                            SettingsManager manager = new SettingsManager(new ArmorPaths(Path.GetTempPath()), __ => null);
                            try
                            {
                                manager.Parse("   ");
                                throw new InvalidOperationException("Expected ArgumentNullException.");
                            }
                            catch (ArgumentNullException)
                            {
                            }
                            return Task.CompletedTask;
                        }),

                    new TestCaseDescriptor(
                        suiteId: "Configuration",
                        caseId: "EnvironmentOverrideApplies",
                        displayName: "Environment overrides win over file values",
                        executeAsync: async ct =>
                        {
                            using (TempWorkspace ws = new TempWorkspace())
                            {
                                Dictionary<string, string?> env = new Dictionary<string, string?>
                                {
                                    { "ARMOR_ENGINE_CONCURRENCY", "16" },
                                    { "ARMOR_SCHEDULER_TICK_SECONDS", "600" }
                                };
                                ArmorPaths paths = new ArmorPaths(ws.Combine("home"));
                                SettingsManager manager = new SettingsManager(paths, name => env.TryGetValue(name, out string? value) ? value : null);

                                ArmorSettings settings = await manager.LoadAsync(ct).ConfigureAwait(false);
                                Check.Equal(16, settings.EngineConcurrency, "engine concurrency overridden");
                                Check.Equal(600, settings.SchedulerTickSeconds, "scheduler tick overridden");
                            }
                        }),

                    new TestCaseDescriptor(
                        suiteId: "Configuration",
                        caseId: "ClampEngineConcurrency",
                        displayName: "Engine concurrency clamps to its maximum",
                        executeAsync: _ =>
                        {
                            ArmorSettings settings = new ArmorSettings();
                            settings.EngineConcurrency = 100000;
                            Check.Equal(64, settings.EngineConcurrency, "engine concurrency clamps high");
                            settings.EngineConcurrency = -5;
                            Check.Equal(1, settings.EngineConcurrency, "engine concurrency clamps low");
                            return Task.CompletedTask;
                        }),

                    new TestCaseDescriptor(
                        suiteId: "Configuration",
                        caseId: "ChunkingOrderingValidated",
                        displayName: "Chunking rejects min greater than avg",
                        executeAsync: _ =>
                        {
                            ArmorSettings settings = new ArmorSettings();
                            settings.Chunking.MinSizeBytes = 4194304;
                            settings.Chunking.AvgSizeBytes = 1048576;
                            settings.Chunking.MaxSizeBytes = 4194304;
                            try
                            {
                                settings.Validate();
                                throw new InvalidOperationException("Expected ArgumentException for inconsistent chunk sizes.");
                            }
                            catch (ArgumentException)
                            {
                            }
                            return Task.CompletedTask;
                        }),

                    new TestCaseDescriptor(
                        suiteId: "Configuration",
                        caseId: "SaveNullThrows",
                        displayName: "Saving null settings throws ArgumentNullException",
                        executeAsync: async ct =>
                        {
                            using (TempWorkspace ws = new TempWorkspace())
                            {
                                SettingsManager manager = new SettingsManager(new ArmorPaths(ws.Combine("home")), _ => null);
                                await Check.ThrowsAsync<ArgumentNullException>(
                                    () => manager.SaveAsync(null!, ct),
                                    "save null should throw").ConfigureAwait(false);
                            }
                        })
                });
        }
    }
}
