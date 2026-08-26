namespace Test.Shared
{
    using System.Collections.Generic;
    using System.IO;
    using System.Threading;
    using System.Threading.Tasks;
    using Armor.Core.Storage;

    /// <summary>
    /// A storage-repository decorator that forwards to an inner repository but throws on the Nth chunk
    /// write, simulating a mid-backup crash so tests can exercise the resume path.
    /// </summary>
    public sealed class FailingRepository : IStorageRepository
    {
        private readonly IStorageRepository _Inner;
        private readonly int _FailAfterChunkWrites;
        private int _ChunkWrites;

        /// <summary>
        /// Initializes a new instance of the <see cref="FailingRepository"/> class.
        /// </summary>
        /// <param name="inner">The repository to forward to. Cannot be null.</param>
        /// <param name="failAfterChunkWrites">Throw on the chunk write with this 1-based index.</param>
        public FailingRepository(IStorageRepository inner, int failAfterChunkWrites)
        {
            _Inner = inner;
            _FailAfterChunkWrites = failAfterChunkWrites;
        }

        /// <inheritdoc/>
        public Task<bool> ValidateConnectionAsync(CancellationToken token = default)
        {
            return _Inner.ValidateConnectionAsync(token);
        }

        /// <inheritdoc/>
        public Task WriteObjectAsync(string key, byte[] data, CancellationToken token = default)
        {
            return _Inner.WriteObjectAsync(key, data, token);
        }

        /// <inheritdoc/>
        public Task<byte[]> ReadObjectAsync(string key, CancellationToken token = default)
        {
            return _Inner.ReadObjectAsync(key, token);
        }

        /// <inheritdoc/>
        public Task<bool> ObjectExistsAsync(string key, CancellationToken token = default)
        {
            return _Inner.ObjectExistsAsync(key, token);
        }

        /// <inheritdoc/>
        public Task DeleteObjectAsync(string key, CancellationToken token = default)
        {
            return _Inner.DeleteObjectAsync(key, token);
        }

        /// <inheritdoc/>
        public IAsyncEnumerable<string> EnumerateKeysAsync(string prefix, CancellationToken token = default)
        {
            return _Inner.EnumerateKeysAsync(prefix, token);
        }

        /// <inheritdoc/>
        public Task WriteChunkAsync(string hashHex, byte[] stored, CancellationToken token = default)
        {
            _ChunkWrites++;
            if (_ChunkWrites == _FailAfterChunkWrites)
                throw new IOException("Simulated storage failure on chunk write " + _ChunkWrites + ".");
            return _Inner.WriteChunkAsync(hashHex, stored, token);
        }

        /// <inheritdoc/>
        public Task<byte[]> ReadChunkAsync(string hashHex, CancellationToken token = default)
        {
            return _Inner.ReadChunkAsync(hashHex, token);
        }

        /// <inheritdoc/>
        public Task<bool> ChunkExistsAsync(string hashHex, CancellationToken token = default)
        {
            return _Inner.ChunkExistsAsync(hashHex, token);
        }

        /// <inheritdoc/>
        public Task DeleteChunkAsync(string hashHex, CancellationToken token = default)
        {
            return _Inner.DeleteChunkAsync(hashHex, token);
        }
    }
}
