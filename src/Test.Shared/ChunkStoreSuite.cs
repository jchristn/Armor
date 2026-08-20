namespace Test.Shared
{
    using System;
    using System.Collections.Generic;
    using System.IO;
    using System.Threading.Tasks;
    using Armor.Core.ChunkStore;
    using Armor.Core.Configuration;
    using Armor.Core.Enums;
    using Armor.Core.Exceptions;
    using Touchstone.Core;

    /// <summary>
    /// Verifies content-defined chunking (determinism, reassembly, edge cases, and boundary
    /// resilience), compression codec selection, and the chunk framer round-trip and tamper handling.
    /// </summary>
    public static class ChunkStoreSuite
    {
        /// <summary>
        /// Build the chunk-store test suite.
        /// </summary>
        /// <returns>The chunk-store suite descriptor.</returns>
        public static TestSuiteDescriptor Build()
        {
            return new TestSuiteDescriptor(
                suiteId: "ChunkStore",
                displayName: "Chunk Store",
                cases: new List<TestCaseDescriptor>
                {
                    Case("ChunkingDeterministic", "Chunking is deterministic and reassembles", _ =>
                    {
                        FastCdc chunker = NewChunker();
                        byte[] input = Pattern(200000, 1);

                        List<string> first = HashesOf(chunker, input);
                        List<string> second = HashesOf(chunker, input);
                        Check.Equal(first.Count, second.Count, "same chunk count across runs");
                        for (int i = 0; i < first.Count; i++)
                            Check.Equal(first[i], second[i], "chunk " + i + " identical across runs");
                        Check.True(first.Count > 1, "input produced multiple chunks");

                        byte[] reassembled = Reassemble(chunker, input);
                        Check.True(Equal(input, reassembled), "chunks reassemble to the original");
                    }),

                    Case("ChunkingEmpty", "Empty stream yields no chunks", _ =>
                    {
                        FastCdc chunker = NewChunker();
                        List<string> hashes = HashesOf(chunker, Array.Empty<byte>());
                        Check.Equal(0, hashes.Count, "no chunks for empty input");
                        return Task.CompletedTask;
                    }),

                    Case("ChunkingSmallFile", "A file below the minimum size is one chunk", _ =>
                    {
                        FastCdc chunker = NewChunker();
                        byte[] input = Pattern(100, 2);
                        List<byte[]> chunks = new List<byte[]>();
                        using (MemoryStream stream = new MemoryStream(input))
                        {
                            foreach (byte[] chunk in chunker.Chunk(stream))
                                chunks.Add(chunk);
                        }
                        Check.Equal(1, chunks.Count, "single chunk for a tiny file");
                        Check.True(Equal(input, chunks[0]), "chunk equals the file");
                        return Task.CompletedTask;
                    }),

                    Case("ChunkingBoundaryResilience", "Inserting a byte re-chunks only locally", _ =>
                    {
                        FastCdc chunker = NewChunker();
                        byte[] original = Pattern(300000, 3);
                        byte[] shifted = new byte[original.Length + 1];
                        shifted[0] = 0x5A;
                        Buffer.BlockCopy(original, 0, shifted, 1, original.Length);

                        HashSet<string> originalHashes = new HashSet<string>(HashesOf(chunker, original), StringComparer.Ordinal);
                        List<string> shiftedHashes = HashesOf(chunker, shifted);

                        int shared = 0;
                        foreach (string hash in shiftedHashes)
                            if (originalHashes.Contains(hash))
                                shared++;

                        Check.True(shared >= originalHashes.Count / 2, "most chunks survive a prefix insertion (shared=" + shared + " of " + originalHashes.Count + ")");
                    }),

                    Case("CompressionCompressible", "Compressible data shrinks and round-trips", _ =>
                    {
                        byte[] input = new byte[50000];
                        for (int i = 0; i < input.Length; i++)
                            input[i] = (byte)'A';
                        CompressedBlock block = Compressor.Compress(input);
                        Check.True(block.Codec != CompressionCodecEnum.None, "a codec was chosen");
                        Check.True(block.Data.Length < input.Length, "compressed data is smaller");
                        byte[] restored = Compressor.Decompress(block.Data, block.Codec);
                        Check.True(Equal(input, restored), "decompression round-trips");
                        return Task.CompletedTask;
                    }),

                    Case("CompressionIncompressible", "Incompressible data is stored uncompressed", _ =>
                    {
                        byte[] input = Pattern(20000, 7);
                        CompressedBlock block = Compressor.Compress(input);
                        Check.Equal(CompressionCodecEnum.None, block.Codec, "random data is not compressed");
                        Check.True(Equal(input, block.Data), "data unchanged when not compressed");
                        return Task.CompletedTask;
                    }),

                    Case("ChunkFramerRoundTrip", "Chunk framer encrypts, compresses, and verifies", _ =>
                    {
                        byte[] key = FixedKey();
                        byte[] plaintext = Pattern(4096, 9);
                        string hash = Hasher.Sha256Hex(plaintext);
                        byte[] stored = ChunkFramer.Frame(plaintext, key, hash);
                        byte[] restored = ChunkFramer.Unframe(stored, key, hash);
                        Check.True(Equal(plaintext, restored), "framer round-trips");
                        return Task.CompletedTask;
                    }),

                    Case("ChunkFramerTamperFails", "Chunk framer rejects tampered storage", _ =>
                    {
                        byte[] key = FixedKey();
                        byte[] plaintext = Pattern(4096, 11);
                        string hash = Hasher.Sha256Hex(plaintext);
                        byte[] stored = ChunkFramer.Frame(plaintext, key, hash);
                        stored[stored.Length - 1] ^= 0xFF;
                        try
                        {
                            ChunkFramer.Unframe(stored, key, hash);
                            throw new InvalidOperationException("Expected ArmorCryptoException.");
                        }
                        catch (ArmorCryptoException)
                        {
                        }
                        return Task.CompletedTask;
                    }),

                    Case("ChunkFramerWrongHashFails", "Chunk framer rejects a mismatched expected hash", _ =>
                    {
                        byte[] key = FixedKey();
                        byte[] plaintext = Pattern(2048, 13);
                        string hash = Hasher.Sha256Hex(plaintext);
                        byte[] stored = ChunkFramer.Frame(plaintext, key, hash);
                        string wrongHash = Hasher.Sha256Hex(Pattern(2048, 14));
                        try
                        {
                            ChunkFramer.Unframe(stored, key, wrongHash);
                            throw new InvalidOperationException("Expected ArmorCryptoException.");
                        }
                        catch (ArmorCryptoException)
                        {
                        }
                        return Task.CompletedTask;
                    })
                });
        }

        private static TestCaseDescriptor Case(string caseId, string displayName, Func<System.Threading.CancellationToken, Task> body)
        {
            return new TestCaseDescriptor(suiteId: "ChunkStore", caseId: caseId, displayName: displayName, executeAsync: body);
        }

        private static TestCaseDescriptor Case(string caseId, string displayName, Action<System.Threading.CancellationToken> body)
        {
            return new TestCaseDescriptor(suiteId: "ChunkStore", caseId: caseId, displayName: displayName, executeAsync: ct =>
            {
                body(ct);
                return Task.CompletedTask;
            });
        }

        private static FastCdc NewChunker()
        {
            ChunkingSettings settings = new ChunkingSettings();
            settings.MinSizeBytes = 1024;
            settings.AvgSizeBytes = 2048;
            settings.MaxSizeBytes = 8192;
            return new FastCdc(settings);
        }

        private static List<string> HashesOf(FastCdc chunker, byte[] input)
        {
            List<string> hashes = new List<string>();
            using (MemoryStream stream = new MemoryStream(input))
            {
                foreach (byte[] chunk in chunker.Chunk(stream))
                    hashes.Add(Hasher.Sha256Hex(chunk));
            }
            return hashes;
        }

        private static byte[] Reassemble(FastCdc chunker, byte[] input)
        {
            using (MemoryStream source = new MemoryStream(input))
            using (MemoryStream sink = new MemoryStream())
            {
                foreach (byte[] chunk in chunker.Chunk(source))
                    sink.Write(chunk, 0, chunk.Length);
                return sink.ToArray();
            }
        }

        private static byte[] Pattern(int length, int seed)
        {
            byte[] data = new byte[length];
            ulong state = (ulong)(seed * 2654435761U) + 0x9E3779B97F4A7C15UL;
            for (int i = 0; i < length; i++)
            {
                state ^= state << 13;
                state ^= state >> 7;
                state ^= state << 17;
                data[i] = (byte)(state & 0xFF);
            }
            return data;
        }

        private static byte[] FixedKey()
        {
            byte[] key = new byte[32];
            for (int i = 0; i < key.Length; i++)
                key[i] = (byte)(i + 1);
            return key;
        }

        private static bool Equal(byte[] a, byte[] b)
        {
            if (a.Length != b.Length)
                return false;
            for (int i = 0; i < a.Length; i++)
                if (a[i] != b[i])
                    return false;
            return true;
        }
    }
}
