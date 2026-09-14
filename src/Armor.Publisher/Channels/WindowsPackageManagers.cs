using System;
using System.Collections.Generic;
using System.IO;

namespace Armor.Publisher.Channels
{
    /// <summary>
    /// Shared helpers for the Windows package-manager channels. They do not build binaries; they
    /// generate manifests that reference the already-built Inno installer assets (URL + SHA-256).
    /// The CI workflow submits them to the central feeds (these are not self-hosted).
    /// </summary>
    internal static class WinPm
    {
        /// <summary>Locates the Inno setup .exe for a rid and returns (fileName, sha256).</summary>
        public static (string fileName, string sha) Installer(ChannelContext ctx, string rid)
        {
            string pattern = $"{ctx.Config.Project.Name}-{ctx.Version}-{rid}-setup.exe";
            string? path = ctx.FindAsset(pattern);
            if (path == null)
                throw new FileNotFoundException(
                    $"Expected Inno installer '{pattern}' in {ctx.OutputDir}. Run the 'inno' channel first " +
                    "(in CI, download the inno artifact into the output dir before this channel).");
            return (Path.GetFileName(path), Checksums.Sha256(path));
        }

        public static string ManifestDir(ChannelContext ctx, string channel)
        {
            string dir = Path.Combine(ctx.OutputDir, "manifests", channel);
            Directory.CreateDirectory(dir);
            return dir;
        }
    }

    /// <summary>Scoop bucket manifest generator. The manifest lives in the repo's bucket/ folder.</summary>
    public sealed class ScoopChannel : IChannel
    {
        /// <inheritdoc/>
        public string Id => "scoop";
        /// <inheritdoc/>
        public string DisplayName => "Scoop manifest (Windows)";
        /// <inheritdoc/>
        public HostOs RequiredHost => HostOs.Any;

        /// <inheritdoc/>
        public IReadOnlyList<string> Build(ChannelContext context)
        {
            (string x64File, string x64Sha) = WinPm.Installer(context, "win-x64");
            (string a64File, string a64Sha) = WinPm.Installer(context, "win-arm64");

            string json =
                "{\n" +
                $"  \"version\": \"{context.Version}\",\n" +
                $"  \"description\": \"{context.Config.Project.Description}\",\n" +
                $"  \"homepage\": \"{context.Config.Project.Homepage}\",\n" +
                $"  \"license\": \"{context.Config.Project.License}\",\n" +
                "  \"architecture\": {\n" +
                $"    \"64bit\": {{ \"url\": \"{context.ReleaseAssetUrl(x64File)}\", \"hash\": \"{x64Sha}\" }},\n" +
                $"    \"arm64\": {{ \"url\": \"{context.ReleaseAssetUrl(a64File)}\", \"hash\": \"{a64Sha}\" }}\n" +
                "  },\n" +
                "  \"innosetup\": true\n" +
                "}\n";

            string name = context.Option("manifestName", context.Config.Project.Name.ToLowerInvariant());
            string outPath = Path.Combine(WinPm.ManifestDir(context, "scoop"), name + ".json");
            File.WriteAllText(outPath, json);
            Console.WriteLine($"[scoop] wrote {outPath} (commit to bucket/ in {context.Config.Destinations.ScoopBucketRepo})");
            return new[] { outPath };
        }
    }

    /// <summary>Chocolatey package generator (.nuspec + chocolateyInstall.ps1). Workflow runs choco pack/push.</summary>
    public sealed class ChocoChannel : IChannel
    {
        /// <inheritdoc/>
        public string Id => "choco";
        /// <inheritdoc/>
        public string DisplayName => "Chocolatey package (Windows)";
        /// <inheritdoc/>
        public HostOs RequiredHost => HostOs.Any;

        /// <inheritdoc/>
        public IReadOnlyList<string> Build(ChannelContext context)
        {
            (string x64File, string x64Sha) = WinPm.Installer(context, "win-x64");
            string pkgName = context.Option("packageName", context.Config.Project.Name.ToLowerInvariant());
            string dir = WinPm.ManifestDir(context, "choco");
            Directory.CreateDirectory(Path.Combine(dir, "tools"));

            string nuspec =
                "<?xml version=\"1.0\" encoding=\"utf-8\"?>\n" +
                "<package xmlns=\"http://schemas.microsoft.com/packaging/2015/06/nuspec.xsd\">\n" +
                "  <metadata>\n" +
                $"    <id>{pkgName}</id>\n" +
                $"    <version>{context.Version}</version>\n" +
                $"    <title>{context.Config.Project.DisplayName}</title>\n" +
                "    <authors>Joel Christner</authors>\n" +
                $"    <projectUrl>{context.Config.Project.Homepage}</projectUrl>\n" +
                $"    <licenseUrl>{context.Config.Project.Repo}/blob/main/LICENSE.md</licenseUrl>\n" +
                "    <requireLicenseAcceptance>false</requireLicenseAcceptance>\n" +
                $"    <description>{context.Config.Project.Description}</description>\n" +
                "  </metadata>\n" +
                "  <files><file src=\"tools\\**\" target=\"tools\" /></files>\n" +
                "</package>\n";
            File.WriteAllText(Path.Combine(dir, pkgName + ".nuspec"), nuspec);

            string ps1 =
                "$ErrorActionPreference = 'Stop'\n" +
                "$packageArgs = @{\n" +
                "  packageName   = '" + pkgName + "'\n" +
                "  fileType      = 'exe'\n" +
                "  url64bit      = '" + context.ReleaseAssetUrl(x64File) + "'\n" +
                "  checksum64    = '" + x64Sha + "'\n" +
                "  checksumType64= 'sha256'\n" +
                "  silentArgs    = '/VERYSILENT /SUPPRESSMSGBOXES /NORESTART'\n" +
                "  validExitCodes= @(0)\n" +
                "}\n" +
                "Install-ChocolateyPackage @packageArgs\n";
            File.WriteAllText(Path.Combine(dir, "tools", "chocolateyInstall.ps1"), ps1);

            Console.WriteLine($"[choco] wrote nuspec + install script in {dir}");
            return new[] { Path.Combine(dir, pkgName + ".nuspec") };
        }
    }

    /// <summary>winget manifest generator (version/installer/locale YAML) for microsoft/winget-pkgs.</summary>
    public sealed class WingetChannel : IChannel
    {
        /// <inheritdoc/>
        public string Id => "winget";
        /// <inheritdoc/>
        public string DisplayName => "winget manifest (Windows)";
        /// <inheritdoc/>
        public HostOs RequiredHost => HostOs.Any;

        /// <inheritdoc/>
        public IReadOnlyList<string> Build(ChannelContext context)
        {
            (string x64File, string x64Sha) = WinPm.Installer(context, "win-x64");
            (string a64File, string a64Sha) = WinPm.Installer(context, "win-arm64");
            string id = context.Option("identifier", context.Config.Project.PackageId);
            string dir = WinPm.ManifestDir(context, "winget");

            File.WriteAllText(Path.Combine(dir, $"{id}.installer.yaml"),
                $"PackageIdentifier: {id}\n" +
                $"PackageVersion: {context.Version}\n" +
                "InstallerType: inno\n" +
                "InstallModes:\n  - silent\n  - silentWithProgress\n" +
                "Installers:\n" +
                $"  - Architecture: x64\n    InstallerUrl: {context.ReleaseAssetUrl(x64File)}\n    InstallerSha256: {x64Sha.ToUpperInvariant()}\n" +
                $"  - Architecture: arm64\n    InstallerUrl: {context.ReleaseAssetUrl(a64File)}\n    InstallerSha256: {a64Sha.ToUpperInvariant()}\n" +
                "ManifestType: installer\nManifestVersion: 1.6.0\n");

            File.WriteAllText(Path.Combine(dir, $"{id}.locale.en-US.yaml"),
                $"PackageIdentifier: {id}\n" +
                $"PackageVersion: {context.Version}\n" +
                "PackageLocale: en-US\n" +
                "Publisher: Joel Christner\n" +
                $"PackageName: {context.Config.Project.DisplayName}\n" +
                $"License: {context.Config.Project.License}\n" +
                $"ShortDescription: {context.Config.Project.Description}\n" +
                "ManifestType: defaultLocale\nManifestVersion: 1.6.0\n");

            File.WriteAllText(Path.Combine(dir, $"{id}.yaml"),
                $"PackageIdentifier: {id}\n" +
                $"PackageVersion: {context.Version}\n" +
                "DefaultLocale: en-US\n" +
                "ManifestType: version\nManifestVersion: 1.6.0\n");

            Console.WriteLine($"[winget] wrote 3 manifests in {dir} (submit to {context.Config.Destinations.WingetPkgsRepo})");
            return new[] { dir };
        }
    }
}
