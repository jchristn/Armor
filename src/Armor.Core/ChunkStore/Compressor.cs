namespace Armor.Core.ChunkStore
{
    using System;
    using System.IO;
    using System.IO.Compression;
    using Armor.Core.Enums;
    using Armor.Core.Exceptions;

    /// <summary>
    /// Compresses and decompresses chunk payloads with BCL codecs. <see cref="Compress"/> tries the
    /// available codecs and keeps the result only if it is smaller than the input, so incompressible
    /// data is never inflated. This type is stateless and thread-safe.
    /// </summary>
    public static class Compressor
    {
        /// <summary>
        /// Compress a block, choosing the codec that produces the smallest output. If no codec shrinks
        /// the input, the block is returned uncompressed with codec <see cref="CompressionCodecEnum.None"/>.
        /// </summary>
        /// <param name="input">The data to compress. Cannot be null.</param>
        /// <returns>The chosen codec and resulting bytes.</returns>
        /// <exception cref="ArgumentNullException">Thrown when <paramref name="input"/> is null.</exception>
        public static CompressedBlock Compress(byte[] input)
        {
            if (input == null)
                throw new ArgumentNullException(nameof(input));

            if (input.Length == 0)
                return new CompressedBlock(CompressionCodecEnum.None, input);

            byte[] deflate = CompressWith(input, CompressionCodecEnum.Deflate);
            byte[] brotli = CompressWith(input, CompressionCodecEnum.Brotli);

            CompressionCodecEnum bestCodec = CompressionCodecEnum.None;
            byte[] best = input;

            if (deflate.Length < best.Length)
            {
                bestCodec = CompressionCodecEnum.Deflate;
                best = deflate;
            }

            if (brotli.Length < best.Length)
            {
                bestCodec = CompressionCodecEnum.Brotli;
                best = brotli;
            }

            return new CompressedBlock(bestCodec, best);
        }

        /// <summary>
        /// Decompress a block produced with the given codec.
        /// </summary>
        /// <param name="data">The (possibly compressed) bytes. Cannot be null.</param>
        /// <param name="codec">The codec used to produce <paramref name="data"/>.</param>
        /// <returns>The decompressed bytes.</returns>
        /// <exception cref="ArgumentNullException">Thrown when <paramref name="data"/> is null.</exception>
        /// <exception cref="ArmorCryptoException">Thrown when the data cannot be decompressed with the stated codec.</exception>
        public static byte[] Decompress(byte[] data, CompressionCodecEnum codec)
        {
            if (data == null)
                throw new ArgumentNullException(nameof(data));

            if (codec == CompressionCodecEnum.None)
                return data;

            try
            {
                using (MemoryStream source = new MemoryStream(data, false))
                using (MemoryStream destination = new MemoryStream())
                {
                    using (Stream decompressor = CreateDecompressor(source, codec))
                    {
                        decompressor.CopyTo(destination);
                    }
                    return destination.ToArray();
                }
            }
            catch (InvalidDataException ex)
            {
                throw new ArmorCryptoException("Chunk payload could not be decompressed with codec " + codec + "; the data may be corrupt.", ex);
            }
        }

        private static byte[] CompressWith(byte[] input, CompressionCodecEnum codec)
        {
            using (MemoryStream destination = new MemoryStream())
            {
                using (Stream compressor = CreateCompressor(destination, codec))
                {
                    compressor.Write(input, 0, input.Length);
                }
                return destination.ToArray();
            }
        }

        private static Stream CreateCompressor(Stream destination, CompressionCodecEnum codec)
        {
            switch (codec)
            {
                case CompressionCodecEnum.Deflate:
                    return new DeflateStream(destination, CompressionLevel.Optimal, true);
                case CompressionCodecEnum.Brotli:
                    return new BrotliStream(destination, CompressionLevel.Optimal, true);
                default:
                    throw new ArgumentException("Unsupported compression codec: " + codec + ".", nameof(codec));
            }
        }

        private static Stream CreateDecompressor(Stream source, CompressionCodecEnum codec)
        {
            switch (codec)
            {
                case CompressionCodecEnum.Deflate:
                    return new DeflateStream(source, CompressionMode.Decompress, true);
                case CompressionCodecEnum.Brotli:
                    return new BrotliStream(source, CompressionMode.Decompress, true);
                default:
                    throw new ArgumentException("Unsupported compression codec: " + codec + ".", nameof(codec));
            }
        }
    }
}
