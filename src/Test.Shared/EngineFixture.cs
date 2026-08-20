namespace Test.Shared
{
    using System;
    using System.IO;
    using System.Threading;
    using System.Threading.Tasks;
    using Armor.Core.Configuration;
    using Armor.Core.Database;
    using Armor.Core.Models;
    using Armor.Core.Security;
    using Armor.Core.Storage;

    /// <summary>
    /// Test fixture wiring a SQLite database, a disk-backed storage repository, and a provisioned data
    /// key for exercising the backup and restore engines. Disposing it closes the database.
    /// </summary>
    public sealed class EngineFixture : IDisposable
    {
        /// <summary>
        /// The database driver.
        /// </summary>
        public DatabaseDriverBase Database { get; private set; } = null!;

        /// <summary>
        /// The storage repository (disk-backed by default).
        /// </summary>
        public IStorageRepository Repository { get; private set; } = null!;

        /// <summary>
        /// The plaintext repository data key.
        /// </summary>
        public byte[] DataKey { get; private set; } = Array.Empty<byte>();

        /// <summary>
        /// The encryption-key entry.
        /// </summary>
        public EncryptionKey EncryptionKey { get; private set; } = null!;

        /// <summary>
        /// The storage-target identifier used to scope the chunk index.
        /// </summary>
        public string StorageTargetId { get; private set; } = "tgt_test";

        /// <summary>
        /// Chunking parameters (small, to produce multiple chunks in tests).
        /// </summary>
        public ChunkingSettings Chunking { get; private set; } = null!;

        private EngineFixture()
        {
        }

        /// <summary>
        /// Build a fixture rooted in the given workspace.
        /// </summary>
        /// <param name="workspace">The temporary workspace. Cannot be null.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>An initialized fixture.</returns>
        public static async Task<EngineFixture> BuildAsync(TempWorkspace workspace, CancellationToken token = default)
        {
            if (workspace == null)
                throw new ArgumentNullException(nameof(workspace));

            EngineFixture fixture = new EngineFixture();

            DatabaseSettings settings = new DatabaseSettings(Path.Combine(workspace.RootDirectory, "armor.db"));
            fixture.Database = await DatabaseDriverFactory.CreateAndInitializeAsync(settings, token).ConfigureAwait(false);

            StorageTarget target = new StorageTarget();
            target.Id = "tgt_test";
            target.Name = "fixture-disk";
            target.Type = Armor.Core.Enums.StorageTargetTypeEnum.Disk;
            target.DiskPath = Path.Combine(workspace.RootDirectory, "repo");
            fixture.Repository = StorageRepositoryFactory.Create(target);
            fixture.StorageTargetId = target.Id;

            Keystore keystore = new Keystore();
            ProvisionedKey provisioned = keystore.Provision("fixture-key", "correct horse battery", null, 50000);
            fixture.DataKey = provisioned.DataKey;
            fixture.EncryptionKey = provisioned.Key;

            ChunkingSettings chunking = new ChunkingSettings();
            chunking.MinSizeBytes = 1024;
            chunking.AvgSizeBytes = 2048;
            chunking.MaxSizeBytes = 8192;
            fixture.Chunking = chunking;

            return fixture;
        }

        /// <summary>
        /// Dispose the fixture, closing the database.
        /// </summary>
        public void Dispose()
        {
            Database?.Dispose();
        }
    }
}
