namespace Armor.Core.Storage
{
    using System.Collections.Generic;
    using System.Threading;
    using System.Threading.Tasks;

    /// <summary>
    /// A repository on a storage target: a keyed object store with conveniences for the header, chunks,
    /// and manifests Armor writes. Implementations wrap a concrete storage provider and apply the
    /// target's repository-root prefix. All keys are provider-neutral and use forward slashes.
    /// </summary>
    public interface IStorageRepository
    {
        /// <summary>
        /// Validate connectivity by writing, reading back, and deleting a small probe object.
        /// </summary>
        /// <param name="token">Cancellation token.</param>
        /// <returns>True if the round-trip succeeds; otherwise false.</returns>
        Task<bool> ValidateConnectionAsync(CancellationToken token = default);

        /// <summary>
        /// Write an object.
        /// </summary>
        /// <param name="key">Object key.</param>
        /// <param name="data">Object bytes.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>A task that completes when the object is written.</returns>
        Task WriteObjectAsync(string key, byte[] data, CancellationToken token = default);

        /// <summary>
        /// Read an object.
        /// </summary>
        /// <param name="key">Object key.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>The object bytes.</returns>
        Task<byte[]> ReadObjectAsync(string key, CancellationToken token = default);

        /// <summary>
        /// Determine whether an object exists.
        /// </summary>
        /// <param name="key">Object key.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>True if the object exists; otherwise false.</returns>
        Task<bool> ObjectExistsAsync(string key, CancellationToken token = default);

        /// <summary>
        /// Delete an object. Deleting a missing object is not an error.
        /// </summary>
        /// <param name="key">Object key.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>A task that completes when the object is deleted or confirmed absent.</returns>
        Task DeleteObjectAsync(string key, CancellationToken token = default);

        /// <summary>
        /// Enumerate object keys beneath a prefix.
        /// </summary>
        /// <param name="prefix">Key prefix, or empty for all keys.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>An async sequence of object keys (relative to the repository root).</returns>
        IAsyncEnumerable<string> EnumerateKeysAsync(string prefix, CancellationToken token = default);

        /// <summary>
        /// Write a stored chunk under its content-addressed key.
        /// </summary>
        /// <param name="hashHex">Lowercase hexadecimal SHA-256 content hash.</param>
        /// <param name="stored">Framed, compressed, and encrypted chunk bytes.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>A task that completes when the chunk is written.</returns>
        Task WriteChunkAsync(string hashHex, byte[] stored, CancellationToken token = default);

        /// <summary>
        /// Read a stored chunk by its content-addressed key.
        /// </summary>
        /// <param name="hashHex">Lowercase hexadecimal SHA-256 content hash.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>The framed chunk bytes.</returns>
        Task<byte[]> ReadChunkAsync(string hashHex, CancellationToken token = default);

        /// <summary>
        /// Determine whether a chunk exists.
        /// </summary>
        /// <param name="hashHex">Lowercase hexadecimal SHA-256 content hash.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>True if the chunk exists; otherwise false.</returns>
        Task<bool> ChunkExistsAsync(string hashHex, CancellationToken token = default);

        /// <summary>
        /// Delete a chunk.
        /// </summary>
        /// <param name="hashHex">Lowercase hexadecimal SHA-256 content hash.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>A task that completes when the chunk is deleted or confirmed absent.</returns>
        Task DeleteChunkAsync(string hashHex, CancellationToken token = default);
    }
}
