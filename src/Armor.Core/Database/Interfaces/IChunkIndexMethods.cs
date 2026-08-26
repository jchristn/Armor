namespace Armor.Core.Database.Interfaces
{
    using System.Collections.Generic;
    using System.Threading;
    using System.Threading.Tasks;
    using Armor.Core.Models;

    /// <summary>
    /// Data-access methods for the per-target chunk index that drives deduplication and
    /// mark-and-sweep garbage collection.
    /// </summary>
    public interface IChunkIndexMethods
    {
        /// <summary>
        /// Read a chunk index entry by target and content hash.
        /// </summary>
        /// <param name="storageTargetId">Storage target identifier.</param>
        /// <param name="hash">Lowercase hexadecimal SHA-256 content hash.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>The entry, or null if the chunk is not indexed on that target.</returns>
        Task<ChunkIndexEntry?> ReadByHashAsync(string storageTargetId, string hash, CancellationToken token = default);

        /// <summary>
        /// Determine whether a chunk exists on a target.
        /// </summary>
        /// <param name="storageTargetId">Storage target identifier.</param>
        /// <param name="hash">Lowercase hexadecimal SHA-256 content hash.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>True if the chunk is indexed on the target; otherwise false.</returns>
        Task<bool> ExistsAsync(string storageTargetId, string hash, CancellationToken token = default);

        /// <summary>
        /// Insert a new chunk index entry, or, if the chunk already exists on the target, increment
        /// its reference count. Sizes are recorded on first insert.
        /// </summary>
        /// <param name="entry">The entry to insert or reference.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>The resulting entry with its current reference count.</returns>
        Task<ChunkIndexEntry> AddOrReferenceAsync(ChunkIndexEntry entry, CancellationToken token = default);

        /// <summary>
        /// Insert-or-reference a batch of chunks in a single transaction. Each entry is inserted with a
        /// reference count of one, or, if the chunk already exists on the target, has its reference count
        /// incremented; sizes are recorded only on first insert. Committing the whole batch at once keeps a
        /// backup from paying a durability fsync per chunk, which is the dominant cost of a large run. The
        /// per-row upsert is atomic, so concurrent batches from parallel workers stay consistent.
        /// </summary>
        /// <param name="entries">The chunk entries to insert or reference. Duplicates within the batch are
        /// applied in order, each incrementing the count.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>A task that completes when the batch is committed.</returns>
        Task ReferenceBatchAsync(IReadOnlyList<ChunkIndexEntry> entries, CancellationToken token = default);

        /// <summary>
        /// Increment a chunk's reference count.
        /// </summary>
        /// <param name="storageTargetId">Storage target identifier.</param>
        /// <param name="hash">Lowercase hexadecimal SHA-256 content hash.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>The new reference count, or -1 if the chunk was not found.</returns>
        Task<long> IncrementReferenceAsync(string storageTargetId, string hash, CancellationToken token = default);

        /// <summary>
        /// Decrement a chunk's reference count, not going below zero.
        /// </summary>
        /// <param name="storageTargetId">Storage target identifier.</param>
        /// <param name="hash">Lowercase hexadecimal SHA-256 content hash.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>The new reference count, or -1 if the chunk was not found.</returns>
        Task<long> DecrementReferenceAsync(string storageTargetId, string hash, CancellationToken token = default);

        /// <summary>
        /// Enumerate chunks on a target whose reference count is zero (eligible for deletion).
        /// </summary>
        /// <param name="storageTargetId">Storage target identifier.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Unreferenced chunk entries.</returns>
        Task<List<ChunkIndexEntry>> ReadUnreferencedAsync(string storageTargetId, CancellationToken token = default);

        /// <summary>
        /// Delete a chunk index entry.
        /// </summary>
        /// <param name="storageTargetId">Storage target identifier.</param>
        /// <param name="hash">Lowercase hexadecimal SHA-256 content hash.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>True if an entry was deleted; false if none matched.</returns>
        Task<bool> DeleteAsync(string storageTargetId, string hash, CancellationToken token = default);
    }
}
