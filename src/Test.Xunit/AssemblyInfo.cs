using Xunit;

// ArmorFactTests and ArmorTheoryTests run the same suites against process-wide state (ArmorTelemetry's
// static settings and its global ActivityListener/MeterListener capture). Running the two classes in
// parallel lets one suite's context reset another's telemetry toggles mid-test, so run them serially.
[assembly: CollectionBehavior(DisableTestParallelization = true)]
