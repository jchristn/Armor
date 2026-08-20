namespace Armor.Core.Configuration
{
    using System;

    /// <summary>
    /// Content-defined chunking parameters. Chunk boundaries are chosen so that most chunks fall
    /// near <see cref="AvgSizeBytes"/>, bounded below by <see cref="MinSizeBytes"/> and above by
    /// <see cref="MaxSizeBytes"/>. Smaller chunks improve deduplication at the cost of more index
    /// entries; larger chunks reduce overhead at the cost of coarser dedup.
    /// </summary>
    public class ChunkingSettings
    {
        private int _MinSizeBytes = 262144;
        private int _AvgSizeBytes = 1048576;
        private int _MaxSizeBytes = 4194304;

        /// <summary>
        /// Minimum chunk size, in bytes. Default is 262144 (256 KiB). Clamped to the range 1024 to
        /// 268435456 (256 MiB).
        /// </summary>
        public int MinSizeBytes
        {
            get
            {
                return _MinSizeBytes;
            }
            set
            {
                _MinSizeBytes = Math.Clamp(value, 1024, 268435456);
            }
        }

        /// <summary>
        /// Target average chunk size, in bytes. Default is 1048576 (1 MiB). Clamped to the range 1024
        /// to 268435456 (256 MiB).
        /// </summary>
        public int AvgSizeBytes
        {
            get
            {
                return _AvgSizeBytes;
            }
            set
            {
                _AvgSizeBytes = Math.Clamp(value, 1024, 268435456);
            }
        }

        /// <summary>
        /// Maximum chunk size, in bytes. Default is 4194304 (4 MiB). Clamped to the range 1024 to
        /// 268435456 (256 MiB).
        /// </summary>
        public int MaxSizeBytes
        {
            get
            {
                return _MaxSizeBytes;
            }
            set
            {
                _MaxSizeBytes = Math.Clamp(value, 1024, 268435456);
            }
        }

        /// <summary>
        /// Initializes a new instance of the <see cref="ChunkingSettings"/> class.
        /// </summary>
        public ChunkingSettings()
        {
        }

        /// <summary>
        /// Validate that the three sizes form a consistent ordering (min &lt;= avg &lt;= max).
        /// </summary>
        /// <exception cref="ArgumentException">Thrown when the sizes are not consistently ordered.</exception>
        public void Validate()
        {
            if (_MinSizeBytes > _AvgSizeBytes)
                throw new ArgumentException("Chunking MinSizeBytes (" + _MinSizeBytes + ") cannot exceed AvgSizeBytes (" + _AvgSizeBytes + ").");
            if (_AvgSizeBytes > _MaxSizeBytes)
                throw new ArgumentException("Chunking AvgSizeBytes (" + _AvgSizeBytes + ") cannot exceed MaxSizeBytes (" + _MaxSizeBytes + ").");
        }
    }
}
