namespace Armor.Core.Models
{
    using System;
    using Armor.Core.Enums;
    using Armor.Core.Helpers;

    /// <summary>
    /// A configured storage destination and the connection material needed to reach it. The set of
    /// meaningful fields depends on <see cref="Type"/>; unused fields are left null. Secret fields
    /// (passwords, keys, service-account JSON) are protected at rest by the storage layer.
    /// </summary>
    public class StorageTarget
    {
        private string _Id = IdGenerator.GenerateStorageTargetId();
        private string _Name = String.Empty;

        /// <summary>
        /// Unique, K-sortable storage-target identifier prefixed with
        /// <see cref="Constants.StorageTargetIdPrefix"/>. Defaults to a freshly generated identifier.
        /// Cannot be null or whitespace.
        /// </summary>
        /// <exception cref="ArgumentNullException">Thrown when set to null or whitespace.</exception>
        public string Id
        {
            get
            {
                return _Id;
            }
            set
            {
                if (String.IsNullOrWhiteSpace(value))
                    throw new ArgumentNullException(nameof(Id));
                _Id = value;
            }
        }

        /// <summary>
        /// Human-readable target name. Cannot be null or whitespace.
        /// </summary>
        /// <exception cref="ArgumentNullException">Thrown when set to null or whitespace.</exception>
        public string Name
        {
            get
            {
                return _Name;
            }
            set
            {
                if (String.IsNullOrWhiteSpace(value))
                    throw new ArgumentNullException(nameof(Name));
                _Name = value;
            }
        }

        /// <summary>
        /// The kind of storage target. Determines which of the connection fields below are used.
        /// Default is <see cref="StorageTargetTypeEnum.Disk"/>.
        /// </summary>
        public StorageTargetTypeEnum Type { get; set; } = StorageTargetTypeEnum.Disk;

        /// <summary>
        /// Optional key prefix (repository root) beneath which Armor writes all objects on the
        /// target. May be null or empty for the target's root.
        /// </summary>
        public string? RepositoryRoot { get; set; } = null;

        /// <summary>
        /// Local filesystem path for <see cref="StorageTargetTypeEnum.Disk"/> targets.
        /// </summary>
        public string? DiskPath { get; set; } = null;

        /// <summary>
        /// Host name for CIFS and NFS targets.
        /// </summary>
        public string? Host { get; set; } = null;

        /// <summary>
        /// Share name for CIFS and NFS targets.
        /// </summary>
        public string? ShareName { get; set; } = null;

        /// <summary>
        /// User name for CIFS targets.
        /// </summary>
        public string? Username { get; set; } = null;

        /// <summary>
        /// Password for CIFS targets. Secret; protected at rest.
        /// </summary>
        public string? Password { get; set; } = null;

        /// <summary>
        /// NFS user id. Default is 0.
        /// </summary>
        public int NfsUserId { get; set; } = 0;

        /// <summary>
        /// NFS group id. Default is 0.
        /// </summary>
        public int NfsGroupId { get; set; } = 0;

        /// <summary>
        /// NFS protocol version string (for example, <c>V3</c>). Used for NFS targets.
        /// </summary>
        public string? NfsVersion { get; set; } = null;

        /// <summary>
        /// Endpoint URL for S3-compatible or Azure targets.
        /// </summary>
        public string? Endpoint { get; set; } = null;

        /// <summary>
        /// Whether to use SSL/TLS when connecting to an S3-compatible endpoint. Default is true.
        /// </summary>
        public bool UseSsl { get; set; } = true;

        /// <summary>
        /// Base URL template for S3-compatible targets (for example, <c>http://host:8000/{bucket}/{key}</c>).
        /// </summary>
        public string? BaseUrl { get; set; } = null;

        /// <summary>
        /// Region for S3 targets.
        /// </summary>
        public string? Region { get; set; } = null;

        /// <summary>
        /// Bucket name for S3 or Google Cloud targets.
        /// </summary>
        public string? Bucket { get; set; } = null;

        /// <summary>
        /// Access key for S3 targets. Secret; protected at rest.
        /// </summary>
        public string? AccessKey { get; set; } = null;

        /// <summary>
        /// Secret key for S3 targets. Secret; protected at rest.
        /// </summary>
        public string? SecretKey { get; set; } = null;

        /// <summary>
        /// Account name for Azure Blob targets.
        /// </summary>
        public string? AccountName { get; set; } = null;

        /// <summary>
        /// Account key for Azure Blob targets. Secret; protected at rest.
        /// </summary>
        public string? AccountKey { get; set; } = null;

        /// <summary>
        /// Container name for Azure Blob targets.
        /// </summary>
        public string? Container { get; set; } = null;

        /// <summary>
        /// Project id for Google Cloud targets.
        /// </summary>
        public string? ProjectId { get; set; } = null;

        /// <summary>
        /// Service-account JSON credentials for Google Cloud targets. Secret; protected at rest.
        /// </summary>
        public string? CredentialJson { get; set; } = null;

        /// <summary>
        /// UTC timestamp when the target was created. Default is the current UTC time.
        /// </summary>
        public DateTime CreatedUtc { get; set; } = DateTime.UtcNow;

        /// <summary>
        /// Initializes a new instance of the <see cref="StorageTarget"/> class.
        /// </summary>
        public StorageTarget()
        {
        }
    }
}
