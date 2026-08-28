namespace Armor.Core.Storage
{
    using System;
    using System.IO;
    using Armor.Core.Enums;
    using Armor.Core.Exceptions;
    using Armor.Core.Models;
    using Blobject.AmazonS3;
    using Blobject.AzureBlob;
    using Blobject.CIFS;
    using Blobject.Core;
    using Blobject.Disk;
    using Blobject.GoogleCloud;
    using Blobject.NFS;

    /// <summary>
    /// Builds an <see cref="IStorageRepository"/> for a configured <see cref="StorageTarget"/> by
    /// constructing the matching Blobject client. Secret fields on the target are expected to already
    /// be in plaintext (decrypted by the caller). This type is stateless and thread-safe.
    /// </summary>
    public static class StorageRepositoryFactory
    {
        /// <summary>
        /// Create a repository for a storage target.
        /// </summary>
        /// <param name="target">The storage target. Cannot be null.</param>
        /// <returns>A repository bound to the target.</returns>
        /// <exception cref="ArgumentNullException">Thrown when <paramref name="target"/> is null.</exception>
        /// <exception cref="ArmorStorageException">Thrown when required fields are missing or the type is unsupported.</exception>
        public static IStorageRepository Create(StorageTarget target)
        {
            if (target == null)
                throw new ArgumentNullException(nameof(target));

            BlobClientBase client = CreateClient(target);

            // For a local-disk target, hand the repository the base directory so it can enumerate a prefix by
            // walking just that subtree on the filesystem. The disk client's own enumeration lists the entire
            // store before filtering, which is unusably slow (it made recovery hang) on a repository holding
            // millions of chunk objects.
            string? localRoot = target.Type == StorageTargetTypeEnum.Disk ? target.DiskPath : null;
            return new BlobStorageRepository(client, target.RepositoryRoot, localRoot);
        }

        private static BlobClientBase CreateClient(StorageTarget target)
        {
            switch (target.Type)
            {
                case StorageTargetTypeEnum.Disk:
                    return CreateDiskClient(target);
                case StorageTargetTypeEnum.AmazonS3:
                    return CreateAmazonS3Client(target);
                case StorageTargetTypeEnum.AzureBlob:
                    return CreateAzureClient(target);
                case StorageTargetTypeEnum.Cifs:
                    return CreateCifsClient(target);
                case StorageTargetTypeEnum.Nfs:
                    return CreateNfsClient(target);
                case StorageTargetTypeEnum.GoogleCloud:
                    return CreateGoogleClient(target);
                default:
                    throw new ArmorStorageException("Unsupported storage target type: " + target.Type + ".");
            }
        }

        private static BlobClientBase CreateDiskClient(StorageTarget target)
        {
            string path = Require(target.DiskPath, "DiskPath", target);
            Directory.CreateDirectory(path);
            return new DiskBlobClient(new DiskSettings(path));
        }

        private static BlobClientBase CreateAmazonS3Client(StorageTarget target)
        {
            string accessKey = Require(target.AccessKey, "AccessKey", target);
            string secretKey = Require(target.SecretKey, "SecretKey", target);
            string region = Require(target.Region, "Region", target);
            string bucket = Require(target.Bucket, "Bucket", target);

            if (!String.IsNullOrWhiteSpace(target.Endpoint))
            {
                string baseUrl = target.BaseUrl ?? String.Empty;
                AwsSettings compatible = new AwsSettings(target.Endpoint, target.UseSsl, accessKey, secretKey, region, bucket, baseUrl);
                return new AmazonS3BlobClient(compatible);
            }

            AwsSettings settings = new AwsSettings(accessKey, secretKey, region, bucket);
            return new AmazonS3BlobClient(settings);
        }

        private static BlobClientBase CreateAzureClient(StorageTarget target)
        {
            string accountName = Require(target.AccountName, "AccountName", target);
            string accountKey = Require(target.AccountKey, "AccountKey", target);
            string endpoint = Require(target.Endpoint, "Endpoint", target);
            string container = Require(target.Container, "Container", target);
            return new AzureBlobClient(new AzureBlobSettings(accountName, accountKey, endpoint, container));
        }

        private static BlobClientBase CreateCifsClient(StorageTarget target)
        {
            string host = Require(target.Host, "Host", target);
            string username = Require(target.Username, "Username", target);
            string password = Require(target.Password, "Password", target);
            string share = Require(target.ShareName, "ShareName", target);
            return new CifsBlobClient(new CifsSettings(host, username, password, share));
        }

        private static BlobClientBase CreateNfsClient(StorageTarget target)
        {
            string host = Require(target.Host, "Host", target);
            string share = Require(target.ShareName, "ShareName", target);
            NfsVersionEnum version = ParseNfsVersion(target.NfsVersion);
            return new NfsBlobClient(new NfsSettings(host, target.NfsUserId, target.NfsGroupId, share, version));
        }

        private static BlobClientBase CreateGoogleClient(StorageTarget target)
        {
            string projectId = Require(target.ProjectId, "ProjectId", target);
            string bucket = Require(target.Bucket, "Bucket", target);
            string credentialJson = Require(target.CredentialJson, "CredentialJson", target);
            return new GcpBlobClient(new GcpBlobSettings(projectId, bucket, credentialJson));
        }

        private static NfsVersionEnum ParseNfsVersion(string? version)
        {
            if (String.IsNullOrWhiteSpace(version))
                return NfsVersionEnum.V3;
            NfsVersionEnum parsed;
            if (Enum.TryParse<NfsVersionEnum>(version, true, out parsed))
                return parsed;
            return NfsVersionEnum.V3;
        }

        private static string Require(string? value, string fieldName, StorageTarget target)
        {
            if (String.IsNullOrWhiteSpace(value))
                throw new ArmorStorageException("Storage target '" + target.Name + "' of type " + target.Type + " is missing required field '" + fieldName + "'.");
            return value;
        }
    }
}
