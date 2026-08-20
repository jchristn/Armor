namespace Armor.Core.Enums
{
    /// <summary>
    /// Identifies how a chunk's payload was compressed before encryption. The codec is recorded in the
    /// chunk frame so decompression is unambiguous.
    /// </summary>
    public enum CompressionCodecEnum
    {
        /// <summary>
        /// No compression; the payload is stored as-is (used when compression would not shrink it).
        /// </summary>
        None = 0,

        /// <summary>
        /// DEFLATE compression (<c>System.IO.Compression.DeflateStream</c>).
        /// </summary>
        Deflate = 1,

        /// <summary>
        /// Brotli compression (<c>System.IO.Compression.BrotliStream</c>).
        /// </summary>
        Brotli = 2
    }
}
