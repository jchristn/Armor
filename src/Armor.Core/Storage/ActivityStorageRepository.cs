namespace Armor.Core.Storage
{
    using System;
    using System.Collections.Generic;
    using System.Runtime.CompilerServices;
    using System.Threading;
    using System.Threading.Tasks;

    /// <summary>
    /// Decorates an <see cref="IStorageRepository"/> so every completed call (successful or not) invokes an
    /// activity callback. A stall watchdog uses it as a heartbeat: a backup that is still talking to its
    /// target is making progress, even inside one very large file. Behavior and exceptions of the inner
    /// repository are passed through unchanged. Thread-safe when the inner repository and callback are.
    /// </summary>
    public sealed class ActivityStorageRepository : IStorageRepository
    {
        private readonly IStorageRepository _Inner;
        private readonly Action _OnActivity;

        /// <summary>
        /// Initializes a new instance of the <see cref="ActivityStorageRepository"/> class.
        /// </summary>
        /// <param name="inner">The repository to forward to. Cannot be null.</param>
        /// <param name="onActivity">Invoked after each call completes. Cannot be null.</param>
        /// <exception cref="ArgumentNullException">Thrown when an argument is null.</exception>
        public ActivityStorageRepository(IStorageRepository inner, Action onActivity)
        {
            _Inner = inner ?? throw new ArgumentNullException(nameof(inner));
            _OnActivity = onActivity ?? throw new ArgumentNullException(nameof(onActivity));
        }

        /// <inheritdoc/>
        public async Task<bool> ValidateConnectionAsync(CancellationToken token = default)
        {
            try { return await _Inner.ValidateConnectionAsync(token).ConfigureAwait(false); }
            finally { _OnActivity(); }
        }

        /// <inheritdoc/>
        public async Task WriteObjectAsync(string key, byte[] data, CancellationToken token = default)
        {
            try { await _Inner.WriteObjectAsync(key, data, token).ConfigureAwait(false); }
            finally { _OnActivity(); }
        }

        /// <inheritdoc/>
        public async Task<byte[]> ReadObjectAsync(string key, CancellationToken token = default)
        {
            try { return await _Inner.ReadObjectAsync(key, token).ConfigureAwait(false); }
            finally { _OnActivity(); }
        }

        /// <inheritdoc/>
        public async Task<bool> ObjectExistsAsync(string key, CancellationToken token = default)
        {
            try { return await _Inner.ObjectExistsAsync(key, token).ConfigureAwait(false); }
            finally { _OnActivity(); }
        }

        /// <inheritdoc/>
        public async Task DeleteObjectAsync(string key, CancellationToken token = default)
        {
            try { await _Inner.DeleteObjectAsync(key, token).ConfigureAwait(false); }
            finally { _OnActivity(); }
        }

        /// <inheritdoc/>
        public async IAsyncEnumerable<string> EnumerateKeysAsync(string prefix, [EnumeratorCancellation] CancellationToken token = default)
        {
            await foreach (string key in _Inner.EnumerateKeysAsync(prefix, token).ConfigureAwait(false))
            {
                _OnActivity();
                yield return key;
            }
            _OnActivity();
        }

        /// <inheritdoc/>
        public async Task WriteChunkAsync(string hashHex, byte[] stored, CancellationToken token = default)
        {
            try { await _Inner.WriteChunkAsync(hashHex, stored, token).ConfigureAwait(false); }
            finally { _OnActivity(); }
        }

        /// <inheritdoc/>
        public async Task<byte[]> ReadChunkAsync(string hashHex, CancellationToken token = default)
        {
            try { return await _Inner.ReadChunkAsync(hashHex, token).ConfigureAwait(false); }
            finally { _OnActivity(); }
        }

        /// <inheritdoc/>
        public async Task<bool> ChunkExistsAsync(string hashHex, CancellationToken token = default)
        {
            try { return await _Inner.ChunkExistsAsync(hashHex, token).ConfigureAwait(false); }
            finally { _OnActivity(); }
        }

        /// <inheritdoc/>
        public async Task DeleteChunkAsync(string hashHex, CancellationToken token = default)
        {
            try { await _Inner.DeleteChunkAsync(hashHex, token).ConfigureAwait(false); }
            finally { _OnActivity(); }
        }
    }
}
