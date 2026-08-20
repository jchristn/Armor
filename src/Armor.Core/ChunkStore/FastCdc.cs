namespace Armor.Core.ChunkStore
{
    using System;
    using System.Collections.Generic;
    using System.IO;
    using System.Runtime.CompilerServices;
    using System.Threading;
    using System.Threading.Tasks;
    using Armor.Core.Configuration;

    /// <summary>
    /// Content-defined chunking using the FastCDC algorithm with normalized cut-point selection. Chunk
    /// boundaries are chosen from content, so inserting or deleting bytes in a file only re-chunks the
    /// affected region, which keeps deduplication effective across edits. The gear table is fixed and
    /// deterministic, so the same input always yields the same chunks on every platform. This type is
    /// immutable after construction and thread-safe.
    /// </summary>
    public sealed class FastCdc
    {
        private static readonly ulong[] _Gear = BuildGearTable();

        private readonly int _MinSize;
        private readonly int _AvgSize;
        private readonly int _MaxSize;
        private readonly ulong _MaskSmall;
        private readonly ulong _MaskLarge;

        /// <summary>
        /// Initializes a new instance of the <see cref="FastCdc"/> class from chunking settings.
        /// </summary>
        /// <param name="settings">Chunking parameters. Cannot be null.</param>
        /// <exception cref="ArgumentNullException">Thrown when <paramref name="settings"/> is null.</exception>
        /// <exception cref="ArgumentException">Thrown when the settings are not consistently ordered.</exception>
        public FastCdc(ChunkingSettings settings)
        {
            if (settings == null)
                throw new ArgumentNullException(nameof(settings));
            settings.Validate();

            _MinSize = settings.MinSizeBytes;
            _AvgSize = settings.AvgSizeBytes;
            _MaxSize = settings.MaxSizeBytes;

            int bits = (int)Math.Round(Math.Log2(_AvgSize));
            _MaskSmall = BuildMask(bits + 2);
            _MaskLarge = BuildMask(bits - 2);
        }

        /// <summary>
        /// Split a stream into content-defined chunks, yielding each chunk's bytes in order.
        /// </summary>
        /// <param name="stream">The source stream, read from its current position. Cannot be null.</param>
        /// <returns>The chunks in order. An empty stream yields no chunks.</returns>
        /// <exception cref="ArgumentNullException">Thrown when <paramref name="stream"/> is null.</exception>
        public IEnumerable<byte[]> Chunk(Stream stream)
        {
            if (stream == null)
                throw new ArgumentNullException(nameof(stream));

            byte[] buffer = new byte[_MaxSize];
            int bufferLength = 0;

            while (true)
            {
                while (bufferLength < _MaxSize)
                {
                    int read = stream.Read(buffer, bufferLength, _MaxSize - bufferLength);
                    if (read == 0)
                        break;
                    bufferLength += read;
                }

                if (bufferLength == 0)
                    yield break;

                int cut = FindCutPoint(buffer, bufferLength);
                byte[] chunk = new byte[cut];
                Buffer.BlockCopy(buffer, 0, chunk, 0, cut);
                yield return chunk;

                int remaining = bufferLength - cut;
                if (remaining > 0)
                    Buffer.BlockCopy(buffer, cut, buffer, 0, remaining);
                bufferLength = remaining;
            }
        }

        /// <summary>
        /// Split a stream into content-defined chunks asynchronously, yielding each chunk's bytes in
        /// order.
        /// </summary>
        /// <param name="stream">The source stream, read from its current position. Cannot be null.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>An async sequence of chunks in order.</returns>
        /// <exception cref="ArgumentNullException">Thrown when <paramref name="stream"/> is null.</exception>
        public async IAsyncEnumerable<byte[]> ChunkAsync(Stream stream, [EnumeratorCancellation] CancellationToken token = default)
        {
            if (stream == null)
                throw new ArgumentNullException(nameof(stream));

            byte[] buffer = new byte[_MaxSize];
            int bufferLength = 0;

            while (true)
            {
                token.ThrowIfCancellationRequested();

                while (bufferLength < _MaxSize)
                {
                    int read = await stream.ReadAsync(buffer.AsMemory(bufferLength, _MaxSize - bufferLength), token).ConfigureAwait(false);
                    if (read == 0)
                        break;
                    bufferLength += read;
                }

                if (bufferLength == 0)
                    yield break;

                int cut = FindCutPoint(buffer, bufferLength);
                byte[] chunk = new byte[cut];
                Buffer.BlockCopy(buffer, 0, chunk, 0, cut);
                yield return chunk;

                int remaining = bufferLength - cut;
                if (remaining > 0)
                    Buffer.BlockCopy(buffer, cut, buffer, 0, remaining);
                bufferLength = remaining;
            }
        }

        private int FindCutPoint(byte[] buffer, int length)
        {
            if (length <= _MinSize)
                return length;

            int limit = Math.Min(length, _MaxSize);
            int normal = Math.Min(limit, _AvgSize);

            ulong fingerprint = 0;
            int i = _MinSize;

            for (; i < normal; i++)
            {
                fingerprint = (fingerprint << 1) + _Gear[buffer[i]];
                if ((fingerprint & _MaskSmall) == 0)
                    return i;
            }

            for (; i < limit; i++)
            {
                fingerprint = (fingerprint << 1) + _Gear[buffer[i]];
                if ((fingerprint & _MaskLarge) == 0)
                    return i;
            }

            return limit;
        }

        private static ulong BuildMask(int bits)
        {
            int clamped = Math.Clamp(bits, 1, 48);
            ulong mask = 0;
            for (int i = 0; i < clamped; i++)
                mask |= 1UL << i;
            return mask;
        }

        private static ulong[] BuildGearTable()
        {
            ulong[] table = new ulong[256];
            ulong state = 0x2545F4914F6CDD1DUL;
            for (int i = 0; i < table.Length; i++)
            {
                state ^= state << 13;
                state ^= state >> 7;
                state ^= state << 17;
                table[i] = state;
            }
            return table;
        }
    }
}
