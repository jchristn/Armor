namespace Armor.Core.ChunkStore
{
    using System;
    using System.Collections.Generic;
    using System.Globalization;
    using System.Text;
    using Armor.Core.Enums;
    using Armor.Core.Exceptions;
    using Armor.Core.Models;
    using Armor.Core.Security;
    using Armor.Core.Serialization;

    /// <summary>
    /// Encodes and decodes manifest objects for storage. Each object is serialized to JSON, compressed,
    /// and encrypted with AES-256-GCM, wrapped in a two-byte <c>[frameVersion][codec]</c> header. Format 1
    /// is a single object holding the whole manifest (legacy); format 2 splits a manifest into a small
    /// header object plus a sequence of numbered segment objects, so a manifest of any size is written and
    /// read without materializing the whole file list — or a single multi-gigabyte buffer — in memory.
    /// This type is stateless and thread-safe.
    /// </summary>
    public static class ManifestCodec
    {
        // Frame versions distinguish the object kinds so a reader can tell them apart from the first byte.
        private const byte FrameVersionWhole = 1;    // Format 1: a whole manifest in one object (legacy).
        private const byte FrameVersionHeader = 2;   // Format 2: the manifest header object.
        private const byte FrameVersionSegment = 3;  // Format 2: a manifest segment object.

        private const int HeaderLengthBytes = 2;
        private const string WholeAssociatedDataPrefix = "armor-manifest:";
        private const string HeaderAssociatedDataPrefix = "armor-manifest-header:";
        private const string SegmentAssociatedDataPrefix = "armor-manifest-seg:";

        /// <summary>
        /// The frame-version byte at the start of a manifest primary object, used to tell a legacy whole
        /// manifest (1) from a segmented manifest's header (2).
        /// </summary>
        /// <param name="stored">The stored primary-object bytes. Cannot be null.</param>
        /// <returns>The frame version byte, or 0 when the object is empty.</returns>
        /// <exception cref="ArgumentNullException">Thrown when <paramref name="stored"/> is null.</exception>
        public static byte FrameVersionOf(byte[] stored)
        {
            if (stored == null)
                throw new ArgumentNullException(nameof(stored));
            return stored.Length == 0 ? (byte)0 : stored[0];
        }

        /// <summary>
        /// True when a stored primary object is a legacy format-1 whole manifest.
        /// </summary>
        /// <param name="stored">The stored primary-object bytes. Cannot be null.</param>
        /// <returns>True for a legacy whole manifest.</returns>
        public static bool IsWholeManifest(byte[] stored)
        {
            return FrameVersionOf(stored) == FrameVersionWhole;
        }

        /// <summary>
        /// Encode a whole manifest into a single stored object (format 1). Retained for compatibility and
        /// for callers with a small, fully in-memory manifest; large manifests are written segmented.
        /// </summary>
        /// <param name="manifest">The manifest. Cannot be null.</param>
        /// <param name="dataKey">The 32-byte repository data key. Cannot be null.</param>
        /// <returns>The stored manifest bytes.</returns>
        /// <exception cref="ArgumentNullException">Thrown when an argument is null.</exception>
        public static byte[] Encode(Manifest manifest, byte[] dataKey)
        {
            if (manifest == null)
                throw new ArgumentNullException(nameof(manifest));
            if (dataKey == null)
                throw new ArgumentNullException(nameof(dataKey));

            return Seal(FrameVersionWhole, ArmorJson.Serialize(manifest), WholeAssociatedDataPrefix + manifest.JobId, dataKey);
        }

        /// <summary>
        /// Decode a stored whole manifest (format 1) back into a <see cref="Manifest"/>.
        /// </summary>
        /// <param name="stored">The stored manifest bytes. Cannot be null.</param>
        /// <param name="dataKey">The 32-byte repository data key. Cannot be null.</param>
        /// <param name="jobId">The job identifier used as associated data when encoding. Cannot be null or whitespace.</param>
        /// <returns>The decoded manifest.</returns>
        /// <exception cref="ArgumentNullException">Thrown when an argument is null.</exception>
        /// <exception cref="ArmorCryptoException">Thrown when the manifest is malformed, fails authentication, or cannot be deserialized.</exception>
        public static Manifest Decode(byte[] stored, byte[] dataKey, string jobId)
        {
            if (String.IsNullOrWhiteSpace(jobId))
                throw new ArgumentNullException(nameof(jobId));

            string json = Open(stored, FrameVersionWhole, WholeAssociatedDataPrefix + jobId, dataKey);
            Manifest? manifest = Deserialize<Manifest>(json);
            if (manifest == null)
                throw new ArmorCryptoException("Decoded manifest deserialized to null.");
            return manifest;
        }

        /// <summary>
        /// Encode a segmented manifest's header object (format 2).
        /// </summary>
        /// <param name="header">The header. Cannot be null.</param>
        /// <param name="dataKey">The 32-byte repository data key. Cannot be null.</param>
        /// <returns>The stored header bytes.</returns>
        /// <exception cref="ArgumentNullException">Thrown when an argument is null.</exception>
        public static byte[] EncodeHeader(ManifestHeader header, byte[] dataKey)
        {
            if (header == null)
                throw new ArgumentNullException(nameof(header));
            if (dataKey == null)
                throw new ArgumentNullException(nameof(dataKey));

            return Seal(FrameVersionHeader, ArmorJson.Serialize(header), HeaderAssociatedDataPrefix + header.JobId, dataKey);
        }

        /// <summary>
        /// Decode a segmented manifest's header object (format 2).
        /// </summary>
        /// <param name="stored">The stored header bytes. Cannot be null.</param>
        /// <param name="dataKey">The 32-byte repository data key. Cannot be null.</param>
        /// <param name="jobId">The job identifier used as associated data. Cannot be null or whitespace.</param>
        /// <returns>The decoded header.</returns>
        /// <exception cref="ArgumentNullException">Thrown when an argument is null.</exception>
        /// <exception cref="ArmorCryptoException">Thrown when the header is malformed, fails authentication, or cannot be deserialized.</exception>
        public static ManifestHeader DecodeHeader(byte[] stored, byte[] dataKey, string jobId)
        {
            if (String.IsNullOrWhiteSpace(jobId))
                throw new ArgumentNullException(nameof(jobId));

            string json = Open(stored, FrameVersionHeader, HeaderAssociatedDataPrefix + jobId, dataKey);
            ManifestHeader? header = Deserialize<ManifestHeader>(json);
            if (header == null)
                throw new ArmorCryptoException("Decoded manifest header deserialized to null.");
            return header;
        }

        /// <summary>
        /// Encode one manifest segment: a batch of file entries (format 2). The segment index is bound into
        /// the tag so a segment cannot be silently reordered or swapped with another run's segment.
        /// </summary>
        /// <param name="entries">The file entries in this segment. Cannot be null.</param>
        /// <param name="jobId">The job identifier. Cannot be null or whitespace.</param>
        /// <param name="segmentIndex">The zero-based index of this segment.</param>
        /// <param name="dataKey">The 32-byte repository data key. Cannot be null.</param>
        /// <returns>The stored segment bytes.</returns>
        /// <exception cref="ArgumentNullException">Thrown when an argument is null.</exception>
        public static byte[] EncodeSegment(IReadOnlyList<ManifestFileEntry> entries, string jobId, int segmentIndex, byte[] dataKey)
        {
            if (entries == null)
                throw new ArgumentNullException(nameof(entries));
            if (String.IsNullOrWhiteSpace(jobId))
                throw new ArgumentNullException(nameof(jobId));
            if (dataKey == null)
                throw new ArgumentNullException(nameof(dataKey));

            return Seal(FrameVersionSegment, ArmorJson.Serialize(entries), SegmentAssociatedData(jobId, segmentIndex), dataKey);
        }

        /// <summary>
        /// Decode one manifest segment back into its file entries (format 2).
        /// </summary>
        /// <param name="stored">The stored segment bytes. Cannot be null.</param>
        /// <param name="jobId">The job identifier. Cannot be null or whitespace.</param>
        /// <param name="segmentIndex">The zero-based index of this segment.</param>
        /// <param name="dataKey">The 32-byte repository data key. Cannot be null.</param>
        /// <returns>The decoded file entries.</returns>
        /// <exception cref="ArgumentNullException">Thrown when an argument is null.</exception>
        /// <exception cref="ArmorCryptoException">Thrown when the segment is malformed, fails authentication, or cannot be deserialized.</exception>
        public static List<ManifestFileEntry> DecodeSegment(byte[] stored, string jobId, int segmentIndex, byte[] dataKey)
        {
            if (String.IsNullOrWhiteSpace(jobId))
                throw new ArgumentNullException(nameof(jobId));

            string json = Open(stored, FrameVersionSegment, SegmentAssociatedData(jobId, segmentIndex), dataKey);
            List<ManifestFileEntry>? entries = Deserialize<List<ManifestFileEntry>>(json);
            if (entries == null)
                throw new ArmorCryptoException("Decoded manifest segment deserialized to null.");
            return entries;
        }

        private static string SegmentAssociatedData(string jobId, int segmentIndex)
        {
            return SegmentAssociatedDataPrefix + jobId + ":" + segmentIndex.ToString(CultureInfo.InvariantCulture);
        }

        private static byte[] Seal(byte frameVersion, string json, string associatedData, byte[] dataKey)
        {
            byte[] plaintext = Encoding.UTF8.GetBytes(json);
            CompressedBlock compressed = Compressor.Compress(plaintext);
            byte[] encrypted = AesGcmCipher.Encrypt(dataKey, compressed.Data, Encoding.UTF8.GetBytes(associatedData));

            byte[] stored = new byte[HeaderLengthBytes + encrypted.Length];
            stored[0] = frameVersion;
            stored[1] = (byte)compressed.Codec;
            Buffer.BlockCopy(encrypted, 0, stored, HeaderLengthBytes, encrypted.Length);
            return stored;
        }

        private static string Open(byte[] stored, byte expectedFrameVersion, string associatedData, byte[] dataKey)
        {
            if (stored == null)
                throw new ArgumentNullException(nameof(stored));
            if (dataKey == null)
                throw new ArgumentNullException(nameof(dataKey));
            if (stored.Length < HeaderLengthBytes)
                throw new ArmorCryptoException("Stored manifest object is too short to be valid.");
            if (stored[0] != expectedFrameVersion)
                throw new ArmorCryptoException("Unexpected stored manifest frame version: " + stored[0] + " (expected " + expectedFrameVersion + ").");

            CompressionCodecEnum codec = (CompressionCodecEnum)stored[1];
            byte[] encrypted = new byte[stored.Length - HeaderLengthBytes];
            Buffer.BlockCopy(stored, HeaderLengthBytes, encrypted, 0, encrypted.Length);

            byte[] compressed = AesGcmCipher.Decrypt(dataKey, encrypted, Encoding.UTF8.GetBytes(associatedData));
            byte[] plaintext = Compressor.Decompress(compressed, codec);
            return Encoding.UTF8.GetString(plaintext);
        }

        private static T? Deserialize<T>(string json) where T : class
        {
            try
            {
                return ArmorJson.Deserialize<T>(json);
            }
            catch (System.Text.Json.JsonException ex)
            {
                throw new ArmorCryptoException("Decoded manifest JSON is malformed.", ex);
            }
        }
    }
}
