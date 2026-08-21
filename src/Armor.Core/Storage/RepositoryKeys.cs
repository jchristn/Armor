namespace Armor.Core.Storage
{
    using System;

    /// <summary>
    /// Builds the object keys used within a repository on a storage target: the repository header, the
    /// content-addressed chunk objects (sharded by the first byte of the hash), and per-policy
    /// manifests. Keys always use forward slashes regardless of platform. This type is stateless and
    /// thread-safe.
    /// </summary>
    public static class RepositoryKeys
    {
        /// <summary>
        /// Key of the repository header object.
        /// </summary>
        public const string HeaderKey = "armor.repo.json";

        /// <summary>
        /// Prefix under which all chunk objects live.
        /// </summary>
        public const string ChunksPrefix = "chunks/";

        /// <summary>
        /// Prefix under which all manifests live.
        /// </summary>
        public const string ManifestsPrefix = "manifests/";

        /// <summary>
        /// Build the object key for a chunk, sharded by the first two hex characters of its hash.
        /// </summary>
        /// <param name="hashHex">Lowercase hexadecimal SHA-256 hash. Cannot be null or shorter than two characters.</param>
        /// <returns>The chunk object key.</returns>
        /// <exception cref="ArgumentException">Thrown when <paramref name="hashHex"/> is null or too short.</exception>
        public static string ChunkKey(string hashHex)
        {
            if (String.IsNullOrWhiteSpace(hashHex) || hashHex.Length < 2)
                throw new ArgumentException("Chunk hash must be at least two characters.", nameof(hashHex));
            return ChunksPrefix + hashHex.Substring(0, 2) + "/" + hashHex;
        }

        /// <summary>
        /// Build the manifest prefix for a policy.
        /// </summary>
        /// <param name="policyId">Policy identifier. Cannot be null or whitespace.</param>
        /// <returns>The manifest prefix for the policy.</returns>
        /// <exception cref="ArgumentNullException">Thrown when <paramref name="policyId"/> is null or whitespace.</exception>
        public static string ManifestPrefix(string policyId)
        {
            if (String.IsNullOrWhiteSpace(policyId))
                throw new ArgumentNullException(nameof(policyId));
            return ManifestsPrefix + policyId + "/";
        }

        /// <summary>
        /// Build the manifest key for a specific run.
        /// </summary>
        /// <param name="policyId">Policy identifier. Cannot be null or whitespace.</param>
        /// <param name="jobId">Backup job identifier. Cannot be null or whitespace.</param>
        /// <returns>The manifest object key.</returns>
        /// <exception cref="ArgumentNullException">Thrown when an argument is null or whitespace.</exception>
        public static string ManifestKey(string policyId, string jobId)
        {
            if (String.IsNullOrWhiteSpace(jobId))
                throw new ArgumentNullException(nameof(jobId));
            return ManifestPrefix(policyId) + jobId + ".manifest";
        }

        /// <summary>
        /// File extension of the manifest object.
        /// </summary>
        public const string ManifestExtension = ".manifest";

        /// <summary>
        /// File extension of the per-run info sidecar object.
        /// </summary>
        public const string InfoExtension = ".info";

        /// <summary>
        /// Build the per-run info-sidecar key for a specific run. The sidecar carries a small encrypted
        /// summary of the run (timestamp, type, file count, sizes) so the catalog can be listed without
        /// decoding the full manifest.
        /// </summary>
        /// <param name="policyId">Policy identifier. Cannot be null or whitespace.</param>
        /// <param name="jobId">Backup job identifier. Cannot be null or whitespace.</param>
        /// <returns>The info object key.</returns>
        /// <exception cref="ArgumentNullException">Thrown when an argument is null or whitespace.</exception>
        public static string InfoKey(string policyId, string jobId)
        {
            if (String.IsNullOrWhiteSpace(jobId))
                throw new ArgumentNullException(nameof(jobId));
            return ManifestPrefix(policyId) + jobId + InfoExtension;
        }
    }
}
