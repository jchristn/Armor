namespace Armor.Core.Enums
{
    /// <summary>
    /// Identifies the kind of storage target a repository is written to. Each value maps to a
    /// concrete Blobject client in the storage layer.
    /// </summary>
    public enum StorageTargetTypeEnum
    {
        /// <summary>
        /// A local filesystem path, including an external USB drive or any mounted volume.
        /// </summary>
        Disk,

        /// <summary>
        /// A CIFS/SMB Windows file share.
        /// </summary>
        Cifs,

        /// <summary>
        /// An NFS export.
        /// </summary>
        Nfs,

        /// <summary>
        /// An Amazon S3 bucket or an S3-compatible endpoint (MinIO, Less3, and similar).
        /// </summary>
        AmazonS3,

        /// <summary>
        /// An Azure Blob Storage container.
        /// </summary>
        AzureBlob,

        /// <summary>
        /// A Google Cloud Storage bucket.
        /// </summary>
        GoogleCloud
    }
}
