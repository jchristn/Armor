using System.Collections.Generic;
using System.IO;
using System.Text.Json;

namespace Armor.Publisher.Channels
{
    /// <summary>Everything a channel needs to build its packages for one invocation.</summary>
    public sealed class ChannelContext
    {
        /// <summary>Absolute path to the repo root (the directory containing publisher.json).</summary>
        public string RepoRoot { get; init; } = "";

        /// <summary>The full parsed configuration.</summary>
        public PublisherConfig Config { get; init; } = new PublisherConfig();

        /// <summary>The channel's own configuration block.</summary>
        public ChannelInfo Channel { get; init; } = new ChannelInfo();

        /// <summary>The artifact this channel packages.</summary>
        public ArtifactInfo Artifact { get; init; } = new ArtifactInfo();

        /// <summary>Version string for this release, e.g. "0.2.0".</summary>
        public string Version { get; init; } = "";

        /// <summary>Runtime identifiers resolved for this channel (e.g. win-x64, win-arm64).</summary>
        public IReadOnlyList<string> Runtimes { get; init; } = new List<string>();

        /// <summary>Target framework moniker to publish (e.g. "net10.0").</summary>
        public string Framework { get; init; } = "";

        /// <summary>Directory where finished packages should be written.</summary>
        public string OutputDir { get; init; } = "";

        /// <summary>Directory for intermediate publish output.</summary>
        public string StagingDir { get; init; } = "";

        /// <summary>Release notes for this version, or empty if none were found.</summary>
        public string ReleaseNotes { get; init; } = "";

        /// <summary>Base name of the artifact's published executable (no extension).</summary>
        public string ExeName => string.IsNullOrEmpty(Artifact.ExeName) ? Artifact.Id : Artifact.ExeName;

        /// <summary>Publishes the channel's artifact for the given rid and returns the output dir.</summary>
        public string PublishPayload(string rid)
        {
            return Payload.Publish(RepoRoot, Artifact, rid, Framework, StagingDir);
        }

        /// <summary>
        /// Publishes the channel's primary artifact plus any artifacts named in the channel's
        /// "include" list, merged into a single payload directory that is returned. Installer
        /// channels use this so one package installs the tray agent and the CLI together. When the
        /// channel includes nothing extra, this is equivalent to <see cref="PublishPayload"/>.
        /// </summary>
        public string PublishCombinedPayload(string rid)
        {
            string primary = PublishPayload(rid);
            if (Channel.Include.Count == 0) return primary;

            string merged = Path.Combine(StagingDir, $"combined-{Artifact.Id}-{rid}");
            if (Directory.Exists(merged)) Directory.Delete(merged, true);
            FileSystemUtil.CopyTree(primary, merged);

            foreach (string id in Channel.Include)
            {
                ArtifactInfo? extra = Config.FindArtifact(id);
                if (extra == null)
                    throw new InvalidDataException($"Channel '{Artifact.Id}' includes unknown artifact id '{id}'.");
                // Distinct exe names coexist; any shared self-contained runtime files are identical.
                FileSystemUtil.CopyTree(Payload.Publish(RepoRoot, extra, rid, Framework, StagingDir), merged);
            }
            return merged;
        }

        /// <summary>Extra artifact ids this channel bundles alongside its primary artifact.</summary>
        public IReadOnlyList<string> IncludedArtifacts => Channel.Include;

        /// <summary>Base executable name (no extension) of an included/other artifact by id.</summary>
        public string ExeNameOf(string artifactId)
        {
            ArtifactInfo? a = Config.FindArtifact(artifactId);
            if (a == null) return artifactId;
            return string.IsNullOrEmpty(a.ExeName) ? a.Id : a.ExeName;
        }

        /// <summary>Reads a string option from the channel's options block, or a default.</summary>
        public string Option(string key, string fallback = "")
        {
            if (Channel.Options.TryGetValue(key, out JsonElement el) && el.ValueKind == JsonValueKind.String)
                return el.GetString() ?? fallback;
            return fallback;
        }

        /// <summary>Writes a checksum sidecar for a produced file and returns its SHA-256.</summary>
        public string EmitChecksum(string file)
        {
            return Checksums.WriteSidecar(file);
        }

        /// <summary>
        /// Resolves the project's license text file at the repo root (LICENSE.md, LICENSE, ...),
        /// or null if none is present. Installer channels show this as a mandatory acceptance page;
        /// package channels bundle it alongside the payload.
        /// </summary>
        public string? LicenseFile()
        {
            foreach (string name in new[] { "LICENSE.md", "LICENSE", "LICENSE.txt", "COPYING", "COPYING.md" })
            {
                string p = Path.Combine(RepoRoot, name);
                if (File.Exists(p)) return p;
            }
            return null;
        }

        /// <summary>
        /// Builds the browser download URL a released asset will have, given the version tag
        /// convention "v&lt;version&gt;" on the configured GitHub repo.
        /// </summary>
        public string ReleaseAssetUrl(string fileName)
        {
            string repo = Config.Destinations.GithubRepo;
            return $"https://github.com/{repo}/releases/download/v{Version}/{fileName}";
        }

        /// <summary>
        /// Finds a previously built installer asset in the output directory matching a search
        /// pattern (e.g. "Armor-*-win-x64-setup.exe"), or null. Package-manager channels use this
        /// to compute checksums of installers the OS jobs already produced.
        /// </summary>
        public string? FindAsset(string searchPattern)
        {
            string[] hits = Directory.GetFiles(OutputDir, searchPattern, SearchOption.AllDirectories);
            return hits.Length > 0 ? hits[0] : null;
        }
    }
}
