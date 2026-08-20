namespace Test.Shared
{
    using System;
    using System.Collections.Generic;
    using System.Threading.Tasks;
    using Armor.Core;
    using Armor.Core.Helpers;
    using Touchstone.Core;

    /// <summary>
    /// Verifies the identifier generator: correct prefixes, uniqueness, and length clamping.
    /// </summary>
    public static class IdentifierSuite
    {
        /// <summary>
        /// Build the identifier test suite.
        /// </summary>
        /// <returns>The identifier suite descriptor.</returns>
        public static TestSuiteDescriptor Build()
        {
            return new TestSuiteDescriptor(
                suiteId: "Identifier",
                displayName: "Identifier Generation",
                cases: new List<TestCaseDescriptor>
                {
                    new TestCaseDescriptor(
                        suiteId: "Identifier",
                        caseId: "PolicyPrefix",
                        displayName: "Policy id carries the policy prefix",
                        executeAsync: _ =>
                        {
                            string id = IdGenerator.GeneratePolicyId();
                            if (!id.StartsWith(Constants.PolicyIdPrefix, StringComparison.Ordinal))
                                throw new InvalidOperationException("Policy id missing prefix: " + id);
                            return Task.CompletedTask;
                        }),

                    new TestCaseDescriptor(
                        suiteId: "Identifier",
                        caseId: "AllPrefixesDistinct",
                        displayName: "Each entity type produces its own prefix",
                        executeAsync: _ =>
                        {
                            AssertPrefix(IdGenerator.GenerateScheduleId(), Constants.ScheduleIdPrefix);
                            AssertPrefix(IdGenerator.GenerateStorageTargetId(), Constants.StorageTargetIdPrefix);
                            AssertPrefix(IdGenerator.GenerateEncryptionKeyId(), Constants.EncryptionKeyIdPrefix);
                            AssertPrefix(IdGenerator.GenerateBackupJobId(), Constants.BackupJobIdPrefix);
                            AssertPrefix(IdGenerator.GenerateRestoreJobId(), Constants.RestoreJobIdPrefix);
                            AssertPrefix(IdGenerator.GenerateChunkId(), Constants.ChunkIdPrefix);
                            return Task.CompletedTask;
                        }),

                    new TestCaseDescriptor(
                        suiteId: "Identifier",
                        caseId: "Uniqueness",
                        displayName: "Generated ids are unique across many draws",
                        executeAsync: _ =>
                        {
                            HashSet<string> seen = new HashSet<string>(StringComparer.Ordinal);
                            for (int i = 0; i < 10000; i++)
                            {
                                string id = IdGenerator.GenerateBackupJobId();
                                if (!seen.Add(id))
                                    throw new InvalidOperationException("Duplicate id generated: " + id);
                            }
                            return Task.CompletedTask;
                        }),

                    new TestCaseDescriptor(
                        suiteId: "Identifier",
                        caseId: "LengthClampLow",
                        displayName: "IdLength below minimum clamps to 16",
                        executeAsync: _ =>
                        {
                            int original = IdGenerator.IdLength;
                            try
                            {
                                IdGenerator.IdLength = 1;
                                if (IdGenerator.IdLength != 16)
                                    throw new InvalidOperationException("Expected clamp to 16, got " + IdGenerator.IdLength);
                            }
                            finally
                            {
                                IdGenerator.IdLength = original;
                            }
                            return Task.CompletedTask;
                        }),

                    new TestCaseDescriptor(
                        suiteId: "Identifier",
                        caseId: "LengthClampHigh",
                        displayName: "IdLength above maximum clamps to 64",
                        executeAsync: _ =>
                        {
                            int original = IdGenerator.IdLength;
                            try
                            {
                                IdGenerator.IdLength = 9999;
                                if (IdGenerator.IdLength != 64)
                                    throw new InvalidOperationException("Expected clamp to 64, got " + IdGenerator.IdLength);
                            }
                            finally
                            {
                                IdGenerator.IdLength = original;
                            }
                            return Task.CompletedTask;
                        })
                });
        }

        private static void AssertPrefix(string id, string prefix)
        {
            if (String.IsNullOrEmpty(id))
                throw new InvalidOperationException("Generated id was null or empty.");
            if (!id.StartsWith(prefix, StringComparison.Ordinal))
                throw new InvalidOperationException("Id '" + id + "' does not start with prefix '" + prefix + "'.");
        }
    }
}
