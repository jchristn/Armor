namespace Armor.Core.Storage
{
    using System;
    using System.Collections.Generic;
    using System.IO;
    using System.Runtime.CompilerServices;
    using System.Security.Cryptography;
    using System.Threading;
    using System.Threading.Tasks;
    using Armor.Core.Exceptions;
    using Blobject.Core;

    /// <summary>
    /// An <see cref="IStorageRepository"/> backed by a Blobject <see cref="BlobClientBase"/>. All keys
    /// are prefixed with the target's repository root (when set) so multiple repositories can share a
    /// target. Thread safety follows the underlying Blobject client.
    /// </summary>
    public sealed class BlobStorageRepository : IStorageRepository
    {
        private const string ContentType = "application/octet-stream";

        private readonly BlobClientBase _Client;
        private readonly string _RootPrefix;

        /// <summary>
        /// Initializes a new instance of the <see cref="BlobStorageRepository"/> class.
        /// </summary>
        /// <param name="client">The Blobject client. Cannot be null.</param>
        /// <param name="repositoryRoot">Optional repository-root key prefix. May be null or empty.</param>
        /// <exception cref="ArgumentNullException">Thrown when <paramref name="client"/> is null.</exception>
        public BlobStorageRepository(BlobClientBase client, string? repositoryRoot)
        {
            _Client = client ?? throw new ArgumentNullException(nameof(client));
            _RootPrefix = NormalizeRoot(repositoryRoot);
        }

        /// <inheritdoc/>
        public async Task<bool> ValidateConnectionAsync(CancellationToken token = default)
        {
            string probeKey = "armor.probe/" + Guid.NewGuid().ToString("N");
            byte[] payload = RandomNumberGenerator.GetBytes(32);

            try
            {
                await WriteObjectAsync(probeKey, payload, token).ConfigureAwait(false);
                byte[] readBack = await ReadObjectAsync(probeKey, token).ConfigureAwait(false);
                bool match = readBack.Length == payload.Length;
                if (match)
                {
                    for (int i = 0; i < payload.Length; i++)
                    {
                        if (readBack[i] != payload[i])
                        {
                            match = false;
                            break;
                        }
                    }
                }

                await DeleteObjectAsync(probeKey, token).ConfigureAwait(false);
                return match;
            }
            catch (Exception) when (!(token.IsCancellationRequested))
            {
                try
                {
                    await DeleteObjectAsync(probeKey, token).ConfigureAwait(false);
                }
                catch (Exception)
                {
                }
                return false;
            }
        }

        /// <inheritdoc/>
        public async Task WriteObjectAsync(string key, byte[] data, CancellationToken token = default)
        {
            if (String.IsNullOrWhiteSpace(key))
                throw new ArgumentNullException(nameof(key));
            if (data == null)
                throw new ArgumentNullException(nameof(data));

            await _Client.WriteAsync(FullKey(key), ContentType, data, token).ConfigureAwait(false);
        }

        /// <inheritdoc/>
        public async Task<byte[]> ReadObjectAsync(string key, CancellationToken token = default)
        {
            if (String.IsNullOrWhiteSpace(key))
                throw new ArgumentNullException(nameof(key));

            try
            {
                return await _Client.GetAsync(FullKey(key), token).ConfigureAwait(false);
            }
            catch (IOException ex)
            {
                throw new ArmorStorageException("Failed to read object '" + key + "' from the storage target.", ex);
            }
        }

        /// <inheritdoc/>
        public async Task<bool> ObjectExistsAsync(string key, CancellationToken token = default)
        {
            if (String.IsNullOrWhiteSpace(key))
                throw new ArgumentNullException(nameof(key));

            return await _Client.ExistsAsync(FullKey(key), token).ConfigureAwait(false);
        }

        /// <inheritdoc/>
        public async Task DeleteObjectAsync(string key, CancellationToken token = default)
        {
            if (String.IsNullOrWhiteSpace(key))
                throw new ArgumentNullException(nameof(key));

            string fullKey = FullKey(key);
            bool exists = await _Client.ExistsAsync(fullKey, token).ConfigureAwait(false);
            if (!exists)
                return;

            await _Client.DeleteAsync(fullKey, token).ConfigureAwait(false);
        }

        /// <inheritdoc/>
        public async IAsyncEnumerable<string> EnumerateKeysAsync(string prefix, [EnumeratorCancellation] CancellationToken token = default)
        {
            EnumerationFilter filter = new EnumerationFilter();
            filter.Prefix = FullKey(prefix ?? String.Empty);

            await foreach (BlobMetadata metadata in _Client.EnumerateAsync(filter, token).ConfigureAwait(false))
            {
                token.ThrowIfCancellationRequested();
                if (metadata.IsFolder)
                    continue;
                if (String.IsNullOrEmpty(metadata.Key))
                    continue;
                yield return StripRoot(metadata.Key);
            }
        }

        /// <inheritdoc/>
        public Task WriteChunkAsync(string hashHex, byte[] stored, CancellationToken token = default)
        {
            return WriteObjectAsync(RepositoryKeys.ChunkKey(hashHex), stored, token);
        }

        /// <inheritdoc/>
        public Task<byte[]> ReadChunkAsync(string hashHex, CancellationToken token = default)
        {
            return ReadObjectAsync(RepositoryKeys.ChunkKey(hashHex), token);
        }

        /// <inheritdoc/>
        public Task<bool> ChunkExistsAsync(string hashHex, CancellationToken token = default)
        {
            return ObjectExistsAsync(RepositoryKeys.ChunkKey(hashHex), token);
        }

        /// <inheritdoc/>
        public Task DeleteChunkAsync(string hashHex, CancellationToken token = default)
        {
            return DeleteObjectAsync(RepositoryKeys.ChunkKey(hashHex), token);
        }

        private string FullKey(string key)
        {
            if (_RootPrefix.Length == 0)
                return key;
            if (key.Length == 0)
                return _RootPrefix;
            return _RootPrefix + key;
        }

        private string StripRoot(string fullKey)
        {
            if (_RootPrefix.Length > 0 && fullKey.StartsWith(_RootPrefix, StringComparison.Ordinal))
                return fullKey.Substring(_RootPrefix.Length);
            return fullKey;
        }

        private static string NormalizeRoot(string? repositoryRoot)
        {
            if (String.IsNullOrWhiteSpace(repositoryRoot))
                return String.Empty;
            string trimmed = repositoryRoot.Replace('\\', '/').Trim('/');
            if (trimmed.Length == 0)
                return String.Empty;
            return trimmed + "/";
        }
    }
}
