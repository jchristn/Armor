namespace Armor.Core.Database.Interfaces
{
    using System.Collections.Generic;
    using System.Threading;
    using System.Threading.Tasks;
    using Armor.Core.Models;

    /// <summary>
    /// Data-access methods for encryption-key entries (the local keystore).
    /// </summary>
    public interface IEncryptionKeyMethods
    {
        /// <summary>
        /// Create an encryption-key entry.
        /// </summary>
        /// <param name="key">The key entry to create. Its identifier is backfilled if empty.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>The created key entry.</returns>
        Task<EncryptionKey> CreateAsync(EncryptionKey key, CancellationToken token = default);

        /// <summary>
        /// Read an encryption-key entry by identifier.
        /// </summary>
        /// <param name="id">Key identifier.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>The key entry, or null if not found.</returns>
        Task<EncryptionKey?> ReadAsync(string id, CancellationToken token = default);

        /// <summary>
        /// Read all encryption-key entries.
        /// </summary>
        /// <param name="token">Cancellation token.</param>
        /// <returns>All key entries, ordered by creation time.</returns>
        Task<List<EncryptionKey>> ReadAllAsync(CancellationToken token = default);

        /// <summary>
        /// Update an encryption-key entry.
        /// </summary>
        /// <param name="key">The key entry to update.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>The updated key entry.</returns>
        Task<EncryptionKey> UpdateAsync(EncryptionKey key, CancellationToken token = default);

        /// <summary>
        /// Delete an encryption-key entry.
        /// </summary>
        /// <param name="id">Key identifier.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>True if a key entry was deleted; false if none matched.</returns>
        Task<bool> DeleteAsync(string id, CancellationToken token = default);

        /// <summary>
        /// Determine whether an encryption-key entry exists.
        /// </summary>
        /// <param name="id">Key identifier.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>True if the key entry exists; otherwise false.</returns>
        Task<bool> ExistsAsync(string id, CancellationToken token = default);
    }
}
