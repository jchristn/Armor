namespace Armor.Core.ChunkStore
{
    using System;
    using System.Collections.Generic;
    using System.Globalization;
    using System.Runtime.CompilerServices;
    using System.Threading;
    using System.Threading.Tasks;
    using Armor.Core.Models;
    using Armor.Core.Storage;

    /// <summary>
    /// Reads and writes manifests on a repository without ever holding the whole file list — or a single
    /// oversized buffer — in memory. A manifest is stored as a small header object at its primary key plus a
    /// sequence of numbered segment objects beside it (<c>&lt;key&gt;.00000000</c>, <c>&lt;key&gt;.00000001</c>,
    /// …), each an independently compressed and encrypted batch of file entries. Reads stream one segment at
    /// a time; writes flush one segment at a time. Legacy format-1 manifests (a single whole-manifest object
    /// at the primary key) are still read transparently. This type is stateless; <see cref="Writer"/> holds
    /// the per-run write state.
    /// </summary>
    public static class ManifestStore
    {
        /// <summary>
        /// Default number of file entries per segment. Bounds the memory a write buffers and a read holds to
        /// one segment's worth of entries, independent of the total file count.
        /// </summary>
        public const int DefaultSegmentSize = 10000;

        /// <summary>
        /// Build the object key of a manifest segment: the primary key followed by a dot and the zero-padded
        /// segment index. The suffix does not end in <c>.manifest</c>, so segment objects are ignored by the
        /// recovery catalog's manifest-key parser.
        /// </summary>
        /// <param name="manifestKey">The manifest primary (header) key. Cannot be null or whitespace.</param>
        /// <param name="segmentIndex">The zero-based segment index.</param>
        /// <returns>The segment object key.</returns>
        /// <exception cref="ArgumentNullException">Thrown when <paramref name="manifestKey"/> is null or whitespace.</exception>
        public static string SegmentKey(string manifestKey, int segmentIndex)
        {
            if (String.IsNullOrWhiteSpace(manifestKey))
                throw new ArgumentNullException(nameof(manifestKey));
            return manifestKey + "." + segmentIndex.ToString("D8", CultureInfo.InvariantCulture);
        }

        /// <summary>
        /// Stream every file entry of a manifest, reading one segment (or, for a legacy manifest, the whole
        /// object) at a time.
        /// </summary>
        /// <param name="repository">The repository. Cannot be null.</param>
        /// <param name="manifestKey">The manifest primary key. Cannot be null or whitespace.</param>
        /// <param name="jobId">The producing job's identifier. Cannot be null or whitespace.</param>
        /// <param name="dataKey">The 32-byte repository data key. Cannot be null.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>An async sequence of the manifest's file entries.</returns>
        /// <exception cref="ArgumentNullException">Thrown when a required argument is null.</exception>
        public static async IAsyncEnumerable<ManifestFileEntry> StreamAsync(
            IStorageRepository repository,
            string manifestKey,
            string jobId,
            byte[] dataKey,
            [EnumeratorCancellation] CancellationToken token = default)
        {
            if (repository == null)
                throw new ArgumentNullException(nameof(repository));
            if (String.IsNullOrWhiteSpace(manifestKey))
                throw new ArgumentNullException(nameof(manifestKey));
            if (String.IsNullOrWhiteSpace(jobId))
                throw new ArgumentNullException(nameof(jobId));

            byte[] primary = await repository.ReadObjectAsync(manifestKey, token).ConfigureAwait(false);

            if (ManifestCodec.IsWholeManifest(primary))
            {
                Manifest legacy = ManifestCodec.Decode(primary, dataKey, jobId);
                foreach (ManifestFileEntry entry in legacy.Files)
                {
                    token.ThrowIfCancellationRequested();
                    yield return entry;
                }
                yield break;
            }

            ManifestHeader header = ManifestCodec.DecodeHeader(primary, dataKey, jobId);
            for (int i = 0; i < header.SegmentCount; i++)
            {
                token.ThrowIfCancellationRequested();
                byte[] segment = await repository.ReadObjectAsync(SegmentKey(manifestKey, i), token).ConfigureAwait(false);
                List<ManifestFileEntry> entries = ManifestCodec.DecodeSegment(segment, jobId, i, dataKey);
                foreach (ManifestFileEntry entry in entries)
                    yield return entry;
            }
        }

        /// <summary>
        /// Read a manifest's header metadata (identity, point-in-time, file/byte counts) without reading any
        /// file entries. For a legacy manifest the whole object is decoded and its counts summarized.
        /// </summary>
        /// <param name="repository">The repository. Cannot be null.</param>
        /// <param name="manifestKey">The manifest primary key. Cannot be null or whitespace.</param>
        /// <param name="jobId">The producing job's identifier. Cannot be null or whitespace.</param>
        /// <param name="dataKey">The 32-byte repository data key. Cannot be null.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>The manifest header metadata.</returns>
        /// <exception cref="ArgumentNullException">Thrown when a required argument is null.</exception>
        public static async Task<ManifestHeader> ReadHeaderAsync(
            IStorageRepository repository,
            string manifestKey,
            string jobId,
            byte[] dataKey,
            CancellationToken token = default)
        {
            if (repository == null)
                throw new ArgumentNullException(nameof(repository));
            if (String.IsNullOrWhiteSpace(manifestKey))
                throw new ArgumentNullException(nameof(manifestKey));
            if (String.IsNullOrWhiteSpace(jobId))
                throw new ArgumentNullException(nameof(jobId));

            byte[] primary = await repository.ReadObjectAsync(manifestKey, token).ConfigureAwait(false);

            if (!ManifestCodec.IsWholeManifest(primary))
                return ManifestCodec.DecodeHeader(primary, dataKey, jobId);

            // Legacy whole manifest: synthesize a header from its contents.
            Manifest legacy = ManifestCodec.Decode(primary, dataKey, jobId);
            long bytes = 0;
            foreach (ManifestFileEntry entry in legacy.Files)
                bytes += entry.SizeBytes;
            return new ManifestHeader
            {
                FormatVersion = 1,
                JobId = String.IsNullOrWhiteSpace(legacy.JobId) ? jobId : legacy.JobId,
                PolicyId = String.IsNullOrWhiteSpace(legacy.PolicyId) ? jobId : legacy.PolicyId,
                BackupType = legacy.BackupType,
                BaseJobId = legacy.BaseJobId,
                PointInTimeUtc = legacy.PointInTimeUtc,
                SegmentCount = 0,
                FileCount = legacy.Files.Count,
                TotalBytes = bytes,
            };
        }

        /// <summary>
        /// Delete a manifest and all of its objects: for a segmented manifest, every segment plus the header;
        /// for a legacy manifest, the single object. Best-effort — a header that cannot be read still has its
        /// primary object deleted.
        /// </summary>
        /// <param name="repository">The repository. Cannot be null.</param>
        /// <param name="manifestKey">The manifest primary key. Cannot be null or whitespace.</param>
        /// <param name="jobId">The producing job's identifier. Cannot be null or whitespace.</param>
        /// <param name="dataKey">The 32-byte repository data key. Cannot be null.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>A task that completes when the objects are deleted.</returns>
        /// <exception cref="ArgumentNullException">Thrown when a required argument is null.</exception>
        public static async Task DeleteAsync(
            IStorageRepository repository,
            string manifestKey,
            string jobId,
            byte[] dataKey,
            CancellationToken token = default)
        {
            if (repository == null)
                throw new ArgumentNullException(nameof(repository));
            if (String.IsNullOrWhiteSpace(manifestKey))
                throw new ArgumentNullException(nameof(manifestKey));

            int segmentCount = 0;
            try
            {
                byte[] primary = await repository.ReadObjectAsync(manifestKey, token).ConfigureAwait(false);
                if (!ManifestCodec.IsWholeManifest(primary) && !String.IsNullOrWhiteSpace(jobId))
                    segmentCount = ManifestCodec.DecodeHeader(primary, dataKey, jobId).SegmentCount;
            }
            catch (Exception)
            {
                // Header unreadable or object missing: fall through and delete what we can by key.
            }

            for (int i = 0; i < segmentCount; i++)
            {
                token.ThrowIfCancellationRequested();
                await repository.DeleteObjectAsync(SegmentKey(manifestKey, i), token).ConfigureAwait(false);
            }
            await repository.DeleteObjectAsync(manifestKey, token).ConfigureAwait(false);
        }

        /// <summary>
        /// Accumulates file entries and flushes them to the repository one segment at a time, then writes the
        /// header last (once the segment count is known). Not thread-safe: a single run writes its manifest
        /// from one thread after processing completes.
        /// </summary>
        public sealed class Writer
        {
            private readonly IStorageRepository _Repository;
            private readonly string _ManifestKey;
            private readonly byte[] _DataKey;
            private readonly ManifestHeader _Header;
            private readonly int _SegmentSize;
            private readonly List<ManifestFileEntry> _Buffer;

            private int _SegmentIndex;
            private long _FileCount;
            private long _TotalBytes;
            private bool _Completed;

            /// <summary>
            /// Initializes a new instance of the <see cref="Writer"/> class.
            /// </summary>
            /// <param name="repository">The repository. Cannot be null.</param>
            /// <param name="manifestKey">The manifest primary key. Cannot be null or whitespace.</param>
            /// <param name="dataKey">The 32-byte repository data key. Cannot be null.</param>
            /// <param name="header">The header metadata (counts are filled in on completion). Cannot be null.</param>
            /// <param name="segmentSize">Entries per segment. Values below 1 use <see cref="DefaultSegmentSize"/>.</param>
            /// <exception cref="ArgumentNullException">Thrown when a required argument is null.</exception>
            public Writer(IStorageRepository repository, string manifestKey, byte[] dataKey, ManifestHeader header, int segmentSize = DefaultSegmentSize)
            {
                _Repository = repository ?? throw new ArgumentNullException(nameof(repository));
                if (String.IsNullOrWhiteSpace(manifestKey))
                    throw new ArgumentNullException(nameof(manifestKey));
                _ManifestKey = manifestKey;
                _DataKey = dataKey ?? throw new ArgumentNullException(nameof(dataKey));
                _Header = header ?? throw new ArgumentNullException(nameof(header));
                _SegmentSize = segmentSize < 1 ? DefaultSegmentSize : segmentSize;
                _Buffer = new List<ManifestFileEntry>(_SegmentSize);
            }

            /// <summary>
            /// Total files added so far.
            /// </summary>
            public long FileCount
            {
                get { return _FileCount; }
            }

            /// <summary>
            /// Total plaintext bytes of files added so far.
            /// </summary>
            public long TotalBytes
            {
                get { return _TotalBytes; }
            }

            /// <summary>
            /// Add one file entry, flushing a full segment to the repository as needed.
            /// </summary>
            /// <param name="entry">The file entry. Cannot be null.</param>
            /// <param name="token">Cancellation token.</param>
            /// <returns>A task that completes when the entry is buffered (and any full segment flushed).</returns>
            /// <exception cref="ArgumentNullException">Thrown when <paramref name="entry"/> is null.</exception>
            public async Task AddAsync(ManifestFileEntry entry, CancellationToken token = default)
            {
                if (entry == null)
                    throw new ArgumentNullException(nameof(entry));

                _Buffer.Add(entry);
                _FileCount++;
                _TotalBytes += entry.SizeBytes;
                if (_Buffer.Count >= _SegmentSize)
                    await FlushSegmentAsync(token).ConfigureAwait(false);
            }

            /// <summary>
            /// Flush the final partial segment (if any) and write the header. Idempotent.
            /// </summary>
            /// <param name="token">Cancellation token.</param>
            /// <returns>A task that completes when the manifest is fully written.</returns>
            public async Task CompleteAsync(CancellationToken token = default)
            {
                if (_Completed)
                    return;

                await FlushSegmentAsync(token).ConfigureAwait(false);

                _Header.SegmentCount = _SegmentIndex;
                _Header.FileCount = _FileCount;
                _Header.TotalBytes = _TotalBytes;
                byte[] headerBytes = ManifestCodec.EncodeHeader(_Header, _DataKey);
                await _Repository.WriteObjectAsync(_ManifestKey, headerBytes, token).ConfigureAwait(false);
                _Completed = true;
            }

            private async Task FlushSegmentAsync(CancellationToken token)
            {
                if (_Buffer.Count == 0)
                    return;

                byte[] segment = ManifestCodec.EncodeSegment(_Buffer, _Header.JobId, _SegmentIndex, _DataKey);
                await _Repository.WriteObjectAsync(SegmentKey(_ManifestKey, _SegmentIndex), segment, token).ConfigureAwait(false);
                _SegmentIndex++;
                _Buffer.Clear();
            }
        }
    }
}
