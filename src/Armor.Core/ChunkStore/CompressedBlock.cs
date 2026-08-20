namespace Armor.Core.ChunkStore
{
    using System;
    using Armor.Core.Enums;

    /// <summary>
    /// The result of compressing a block: the codec chosen and the resulting bytes. When compression
    /// would not shrink the input, the codec is <see cref="CompressionCodecEnum.None"/> and the bytes
    /// are the original data.
    /// </summary>
    public class CompressedBlock
    {
        /// <summary>
        /// The codec applied to produce <see cref="Data"/>.
        /// </summary>
        public CompressionCodecEnum Codec { get; }

        /// <summary>
        /// The (possibly compressed) bytes. Never null.
        /// </summary>
        public byte[] Data { get; }

        /// <summary>
        /// Initializes a new instance of the <see cref="CompressedBlock"/> class.
        /// </summary>
        /// <param name="codec">The codec applied.</param>
        /// <param name="data">The resulting bytes. Cannot be null.</param>
        /// <exception cref="ArgumentNullException">Thrown when <paramref name="data"/> is null.</exception>
        public CompressedBlock(CompressionCodecEnum codec, byte[] data)
        {
            Codec = codec;
            Data = data ?? throw new ArgumentNullException(nameof(data));
        }
    }
}
