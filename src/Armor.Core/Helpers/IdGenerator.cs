namespace Armor.Core.Helpers
{
    using System;
    using PrettyId;

    /// <summary>
    /// Generates K-sortable, prefixed string identifiers for Armor domain entities. Identifiers
    /// are lexically sortable by creation time, which keeps naturally ordered listings cheap.
    /// This type is thread-safe: the underlying <see cref="PrettyId.IdGenerator"/> is stateless
    /// per call and guarded only by immutable configuration.
    /// </summary>
    public static class IdGenerator
    {
        private static readonly PrettyId.IdGenerator _Generator = new PrettyId.IdGenerator();
        private static int _IdLength = 24;

        /// <summary>
        /// Total identifier length, including the entity prefix. Default is 24. Minimum is 16;
        /// maximum is 64. Shorter values increase collision probability; longer values waste space.
        /// </summary>
        public static int IdLength
        {
            get
            {
                return _IdLength;
            }
            set
            {
                _IdLength = Math.Clamp(value, 16, 64);
            }
        }

        /// <summary>
        /// Generate a new policy identifier prefixed with <see cref="Constants.PolicyIdPrefix"/>.
        /// </summary>
        /// <returns>A new K-sortable policy identifier.</returns>
        public static string GeneratePolicyId()
        {
            return _Generator.GenerateKSortable(Constants.PolicyIdPrefix, _IdLength);
        }

        /// <summary>
        /// Generate a new schedule identifier prefixed with <see cref="Constants.ScheduleIdPrefix"/>.
        /// </summary>
        /// <returns>A new K-sortable schedule identifier.</returns>
        public static string GenerateScheduleId()
        {
            return _Generator.GenerateKSortable(Constants.ScheduleIdPrefix, _IdLength);
        }

        /// <summary>
        /// Generate a new storage-target identifier prefixed with <see cref="Constants.StorageTargetIdPrefix"/>.
        /// </summary>
        /// <returns>A new K-sortable storage-target identifier.</returns>
        public static string GenerateStorageTargetId()
        {
            return _Generator.GenerateKSortable(Constants.StorageTargetIdPrefix, _IdLength);
        }

        /// <summary>
        /// Generate a new encryption-key identifier prefixed with <see cref="Constants.EncryptionKeyIdPrefix"/>.
        /// </summary>
        /// <returns>A new K-sortable encryption-key identifier.</returns>
        public static string GenerateEncryptionKeyId()
        {
            return _Generator.GenerateKSortable(Constants.EncryptionKeyIdPrefix, _IdLength);
        }

        /// <summary>
        /// Generate a new backup-job identifier prefixed with <see cref="Constants.BackupJobIdPrefix"/>.
        /// </summary>
        /// <returns>A new K-sortable backup-job identifier.</returns>
        public static string GenerateBackupJobId()
        {
            return _Generator.GenerateKSortable(Constants.BackupJobIdPrefix, _IdLength);
        }

        /// <summary>
        /// Generate a new restore-job identifier prefixed with <see cref="Constants.RestoreJobIdPrefix"/>.
        /// </summary>
        /// <returns>A new K-sortable restore-job identifier.</returns>
        public static string GenerateRestoreJobId()
        {
            return _Generator.GenerateKSortable(Constants.RestoreJobIdPrefix, _IdLength);
        }

        /// <summary>
        /// Generate a new chunk-index identifier prefixed with <see cref="Constants.ChunkIdPrefix"/>.
        /// </summary>
        /// <returns>A new K-sortable chunk-index identifier.</returns>
        public static string GenerateChunkId()
        {
            return _Generator.GenerateKSortable(Constants.ChunkIdPrefix, _IdLength);
        }
    }
}
