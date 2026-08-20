namespace Test.Shared
{
    using System;
    using Armor.Core.Enums;
    using Armor.Core.Models;

    /// <summary>
    /// Resolves the storage target used by the storage tests. By default the tests run against a local
    /// disk directory inside a temporary workspace, which is cleaned up afterward. When
    /// <c>ARMOR_TEST_STORAGE_TYPE</c> and the matching <c>ARMOR_TEST_*</c> variables are set, the tests
    /// run against that real provider instead, isolated under a unique repository-root prefix.
    /// </summary>
    public static class TestStorage
    {
        /// <summary>
        /// Build a storage target for a test, defaulting to a disk directory under the workspace.
        /// </summary>
        /// <param name="workspace">The temporary workspace for disk-backed runs. Cannot be null.</param>
        /// <param name="uniquePrefix">A unique repository-root prefix isolating this test's objects.</param>
        /// <returns>A configured storage target.</returns>
        /// <exception cref="ArgumentNullException">Thrown when <paramref name="workspace"/> is null.</exception>
        public static StorageTarget Resolve(TempWorkspace workspace, string uniquePrefix)
        {
            if (workspace == null)
                throw new ArgumentNullException(nameof(workspace));

            string? type = Environment.GetEnvironmentVariable("ARMOR_TEST_STORAGE_TYPE");
            if (String.IsNullOrWhiteSpace(type))
            {
                StorageTarget disk = new StorageTarget();
                disk.Name = "test-disk";
                disk.Type = StorageTargetTypeEnum.Disk;
                disk.DiskPath = workspace.Combine("repo");
                disk.RepositoryRoot = uniquePrefix;
                return disk;
            }

            StorageTarget target = new StorageTarget();
            target.Name = "test-" + type;
            target.RepositoryRoot = CombinePrefix(Environment.GetEnvironmentVariable("ARMOR_TEST_PREFIX"), uniquePrefix);

            switch (type.ToLowerInvariant())
            {
                case "s3":
                case "amazons3":
                    target.Type = StorageTargetTypeEnum.AmazonS3;
                    target.AccessKey = Environment.GetEnvironmentVariable("ARMOR_TEST_S3_ACCESS_KEY");
                    target.SecretKey = Environment.GetEnvironmentVariable("ARMOR_TEST_S3_SECRET_KEY");
                    target.Region = Environment.GetEnvironmentVariable("ARMOR_TEST_S3_REGION");
                    target.Bucket = Environment.GetEnvironmentVariable("ARMOR_TEST_S3_BUCKET");
                    target.Endpoint = Environment.GetEnvironmentVariable("ARMOR_TEST_S3_ENDPOINT");
                    target.BaseUrl = Environment.GetEnvironmentVariable("ARMOR_TEST_S3_BASE_URL");
                    break;
                case "azure":
                case "azureblob":
                    target.Type = StorageTargetTypeEnum.AzureBlob;
                    target.AccountName = Environment.GetEnvironmentVariable("ARMOR_TEST_AZURE_ACCOUNT");
                    target.AccountKey = Environment.GetEnvironmentVariable("ARMOR_TEST_AZURE_KEY");
                    target.Endpoint = Environment.GetEnvironmentVariable("ARMOR_TEST_AZURE_ENDPOINT");
                    target.Container = Environment.GetEnvironmentVariable("ARMOR_TEST_AZURE_CONTAINER");
                    break;
                case "gcp":
                case "googlecloud":
                    target.Type = StorageTargetTypeEnum.GoogleCloud;
                    target.ProjectId = Environment.GetEnvironmentVariable("ARMOR_TEST_GCP_PROJECT");
                    target.Bucket = Environment.GetEnvironmentVariable("ARMOR_TEST_GCP_BUCKET");
                    target.CredentialJson = Environment.GetEnvironmentVariable("ARMOR_TEST_GCP_CREDENTIAL_JSON");
                    break;
                case "cifs":
                    target.Type = StorageTargetTypeEnum.Cifs;
                    target.Host = Environment.GetEnvironmentVariable("ARMOR_TEST_CIFS_HOST");
                    target.Username = Environment.GetEnvironmentVariable("ARMOR_TEST_CIFS_USERNAME");
                    target.Password = Environment.GetEnvironmentVariable("ARMOR_TEST_CIFS_PASSWORD");
                    target.ShareName = Environment.GetEnvironmentVariable("ARMOR_TEST_CIFS_SHARE");
                    break;
                case "nfs":
                    target.Type = StorageTargetTypeEnum.Nfs;
                    target.Host = Environment.GetEnvironmentVariable("ARMOR_TEST_NFS_HOST");
                    target.ShareName = Environment.GetEnvironmentVariable("ARMOR_TEST_NFS_SHARE");
                    target.NfsVersion = Environment.GetEnvironmentVariable("ARMOR_TEST_NFS_VERSION");
                    break;
                default:
                    throw new NotSupportedException("Unknown ARMOR_TEST_STORAGE_TYPE: " + type);
            }

            return target;
        }

        private static string CombinePrefix(string? basePrefix, string uniquePrefix)
        {
            if (String.IsNullOrWhiteSpace(basePrefix))
                return uniquePrefix;
            return basePrefix.Trim('/') + "/" + uniquePrefix;
        }
    }
}
