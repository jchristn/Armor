namespace Armor.Core.Database.Sqlite.Implementations
{
    using System;
    using System.Collections.Generic;
    using System.Data;
    using System.Threading;
    using System.Threading.Tasks;
    using Armor.Core.Database.Interfaces;
    using Armor.Core.Helpers;
    using Armor.Core.Models;

    /// <summary>
    /// SQLite implementation of <see cref="IChunkIndexMethods"/>. Reference counting is performed with
    /// atomic single-statement upserts and updates so deduplication and garbage collection see a
    /// consistent count.
    /// </summary>
    public sealed class SqliteChunkIndexMethods : IChunkIndexMethods
    {
        private readonly DatabaseDriverBase _Driver;

        /// <summary>
        /// Initializes a new instance of the <see cref="SqliteChunkIndexMethods"/> class.
        /// </summary>
        /// <param name="driver">The owning database driver. Cannot be null.</param>
        /// <exception cref="ArgumentNullException">Thrown when <paramref name="driver"/> is null.</exception>
        public SqliteChunkIndexMethods(DatabaseDriverBase driver)
        {
            _Driver = driver ?? throw new ArgumentNullException(nameof(driver));
        }

        /// <inheritdoc/>
        public async Task<ChunkIndexEntry?> ReadByHashAsync(string storageTargetId, string hash, CancellationToken token = default)
        {
            if (String.IsNullOrWhiteSpace(storageTargetId))
                throw new ArgumentNullException(nameof(storageTargetId));
            if (String.IsNullOrWhiteSpace(hash))
                throw new ArgumentNullException(nameof(hash));

            DataTable table = await _Driver.ExecuteQueryAsync(
                "SELECT * FROM chunk_index WHERE storage_target_id = " + Sanitizer.Literal(storageTargetId) +
                " AND hash = " + Sanitizer.Literal(hash) + ";", false, token).ConfigureAwait(false);

            return table.Rows.Count == 0 ? null : MapRow(table.Rows[0]);
        }

        /// <inheritdoc/>
        public async Task<bool> ExistsAsync(string storageTargetId, string hash, CancellationToken token = default)
        {
            if (String.IsNullOrWhiteSpace(storageTargetId))
                throw new ArgumentNullException(nameof(storageTargetId));
            if (String.IsNullOrWhiteSpace(hash))
                throw new ArgumentNullException(nameof(hash));

            DataTable table = await _Driver.ExecuteQueryAsync(
                "SELECT COUNT(*) AS count FROM chunk_index WHERE storage_target_id = " + Sanitizer.Literal(storageTargetId) +
                " AND hash = " + Sanitizer.Literal(hash) + ";", false, token).ConfigureAwait(false);

            return table.Rows.Count > 0 && Converters.GetLong(table.Rows[0], "count") > 0;
        }

        /// <inheritdoc/>
        public async Task<ChunkIndexEntry> AddOrReferenceAsync(ChunkIndexEntry entry, CancellationToken token = default)
        {
            if (entry == null)
                throw new ArgumentNullException(nameof(entry));
            if (String.IsNullOrWhiteSpace(entry.StorageTargetId))
                throw new ArgumentException("StorageTargetId is required.", nameof(entry));
            if (String.IsNullOrWhiteSpace(entry.Hash))
                throw new ArgumentException("Hash is required.", nameof(entry));
            if (String.IsNullOrWhiteSpace(entry.Id))
                entry.Id = IdGenerator.GenerateChunkId();

            await _Driver.ExecuteQueryAsync(
                "INSERT INTO chunk_index (id, storage_target_id, hash, stored_size_bytes, plaintext_size_bytes, reference_count, created_utc) VALUES (" +
                Sanitizer.Literal(entry.Id) + ", " +
                Sanitizer.Literal(entry.StorageTargetId) + ", " +
                Sanitizer.Literal(entry.Hash) + ", " +
                Sanitizer.Int(entry.StoredSizeBytes) + ", " +
                Sanitizer.Int(entry.PlaintextSizeBytes) + ", " +
                "1, " +
                Sanitizer.Timestamp(entry.CreatedUtc) + ") " +
                "ON CONFLICT(storage_target_id, hash) DO UPDATE SET reference_count = reference_count + 1;", false, token).ConfigureAwait(false);

            ChunkIndexEntry? stored = await ReadByHashAsync(entry.StorageTargetId, entry.Hash, token).ConfigureAwait(false);
            if (stored == null)
                throw new InvalidOperationException("Chunk index entry disappeared immediately after upsert.");
            return stored;
        }

        /// <inheritdoc/>
        public async Task<long> IncrementReferenceAsync(string storageTargetId, string hash, CancellationToken token = default)
        {
            if (String.IsNullOrWhiteSpace(storageTargetId))
                throw new ArgumentNullException(nameof(storageTargetId));
            if (String.IsNullOrWhiteSpace(hash))
                throw new ArgumentNullException(nameof(hash));

            await _Driver.ExecuteQueryAsync(
                "UPDATE chunk_index SET reference_count = reference_count + 1 WHERE storage_target_id = " + Sanitizer.Literal(storageTargetId) +
                " AND hash = " + Sanitizer.Literal(hash) + ";", false, token).ConfigureAwait(false);

            return await ReadReferenceCountAsync(storageTargetId, hash, token).ConfigureAwait(false);
        }

        /// <inheritdoc/>
        public async Task<long> DecrementReferenceAsync(string storageTargetId, string hash, CancellationToken token = default)
        {
            if (String.IsNullOrWhiteSpace(storageTargetId))
                throw new ArgumentNullException(nameof(storageTargetId));
            if (String.IsNullOrWhiteSpace(hash))
                throw new ArgumentNullException(nameof(hash));

            await _Driver.ExecuteQueryAsync(
                "UPDATE chunk_index SET reference_count = MAX(reference_count - 1, 0) WHERE storage_target_id = " + Sanitizer.Literal(storageTargetId) +
                " AND hash = " + Sanitizer.Literal(hash) + ";", false, token).ConfigureAwait(false);

            return await ReadReferenceCountAsync(storageTargetId, hash, token).ConfigureAwait(false);
        }

        /// <inheritdoc/>
        public async Task<List<ChunkIndexEntry>> ReadUnreferencedAsync(string storageTargetId, CancellationToken token = default)
        {
            if (String.IsNullOrWhiteSpace(storageTargetId))
                throw new ArgumentNullException(nameof(storageTargetId));

            DataTable table = await _Driver.ExecuteQueryAsync(
                "SELECT * FROM chunk_index WHERE storage_target_id = " + Sanitizer.Literal(storageTargetId) +
                " AND reference_count <= 0 ORDER BY id ASC;", false, token).ConfigureAwait(false);

            List<ChunkIndexEntry> list = new List<ChunkIndexEntry>();
            foreach (DataRow row in table.Rows)
                list.Add(MapRow(row));
            return list;
        }

        /// <inheritdoc/>
        public async Task<bool> DeleteAsync(string storageTargetId, string hash, CancellationToken token = default)
        {
            if (String.IsNullOrWhiteSpace(storageTargetId))
                throw new ArgumentNullException(nameof(storageTargetId));
            if (String.IsNullOrWhiteSpace(hash))
                throw new ArgumentNullException(nameof(hash));

            if (!await ExistsAsync(storageTargetId, hash, token).ConfigureAwait(false))
                return false;

            await _Driver.ExecuteQueryAsync(
                "DELETE FROM chunk_index WHERE storage_target_id = " + Sanitizer.Literal(storageTargetId) +
                " AND hash = " + Sanitizer.Literal(hash) + ";", false, token).ConfigureAwait(false);
            return true;
        }

        private async Task<long> ReadReferenceCountAsync(string storageTargetId, string hash, CancellationToken token)
        {
            DataTable table = await _Driver.ExecuteQueryAsync(
                "SELECT reference_count FROM chunk_index WHERE storage_target_id = " + Sanitizer.Literal(storageTargetId) +
                " AND hash = " + Sanitizer.Literal(hash) + ";", false, token).ConfigureAwait(false);

            if (table.Rows.Count == 0)
                return -1;
            return Converters.GetLong(table.Rows[0], "reference_count");
        }

        private static ChunkIndexEntry MapRow(DataRow row)
        {
            ChunkIndexEntry entry = new ChunkIndexEntry();
            entry.Id = Converters.GetString(row, "id");
            entry.StorageTargetId = Converters.GetString(row, "storage_target_id");
            entry.Hash = Converters.GetString(row, "hash");
            entry.StoredSizeBytes = Converters.GetLong(row, "stored_size_bytes");
            entry.PlaintextSizeBytes = Converters.GetLong(row, "plaintext_size_bytes");
            entry.ReferenceCount = Converters.GetLong(row, "reference_count");
            entry.CreatedUtc = Converters.GetDateTime(row, "created_utc");
            return entry;
        }
    }
}
