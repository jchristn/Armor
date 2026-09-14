using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Armor.Publisher
{
    /// <summary>
    /// Strongly-typed view of publisher.json. Property names map to the camelCase keys in the
    /// file via a case-insensitive, camelCase-naming deserializer (see <see cref="Load"/>).
    /// </summary>
    public sealed class PublisherConfig
    {
        /// <summary>Schema version of the publisher.json document.</summary>
        public int SchemaVersion { get; set; }

        /// <summary>Project identity (name, repo, license, ...).</summary>
        public ProjectInfo Project { get; set; } = new ProjectInfo();

        /// <summary>What to build: the artifacts, target frameworks, and runtimes.</summary>
        public BuildInfo Build { get; set; } = new BuildInfo();

        /// <summary>Where each channel publishes (repos, hosts, registries).</summary>
        public DestinationsInfo Destinations { get; set; } = new DestinationsInfo();

        /// <summary>Packaging channels keyed by id ("inno", "dmg", "debrpm", ...).</summary>
        public Dictionary<string, ChannelInfo> Channels { get; set; } = new Dictionary<string, ChannelInfo>();

        /// <summary>Per-OS code-signing configuration.</summary>
        public SigningInfo Signing { get; set; } = new SigningInfo();

        /// <summary>Where release notes come from.</summary>
        public ReleaseNotesInfo ReleaseNotes { get; set; } = new ReleaseNotesInfo();

        private static readonly JsonSerializerOptions Options = new JsonSerializerOptions
        {
            PropertyNameCaseInsensitive = true,
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            ReadCommentHandling = JsonCommentHandling.Skip,
            AllowTrailingCommas = true
        };

        /// <summary>Loads and validates a publisher.json file from disk.</summary>
        public static PublisherConfig Load(string path)
        {
            string json = File.ReadAllText(path);
            PublisherConfig? config = JsonSerializer.Deserialize<PublisherConfig>(json, Options);
            if (config == null) throw new InvalidDataException($"publisher.json at '{path}' deserialized to null.");
            return config;
        }

        /// <summary>Returns the artifact with the given id, or null if it is not declared.</summary>
        public ArtifactInfo? FindArtifact(string id)
        {
            foreach (ArtifactInfo a in Build.Artifacts)
                if (string.Equals(a.Id, id, System.StringComparison.OrdinalIgnoreCase)) return a;
            return null;
        }
    }

    /// <summary>Project identity block.</summary>
    public sealed class ProjectInfo
    {
        public string Name { get; set; } = "";
        public string DisplayName { get; set; } = "";

        /// <summary>Reverse-DNS-ish package identifier used by winget/dotnet-tool/NuGet.</summary>
        public string PackageId { get; set; } = "";
        public string Repo { get; set; } = "";
        public string Homepage { get; set; } = "";
        public string Description { get; set; } = "";
        public string License { get; set; } = "";

        /// <summary>Maintainer "Name &lt;email&gt;" for deb/rpm control metadata.</summary>
        public string Maintainer { get; set; } = "";
    }

    /// <summary>Publish destinations shared across channels.</summary>
    public sealed class DestinationsInfo
    {
        public string GithubRepo { get; set; } = "";
        public string GithubPagesUrl { get; set; } = "";
        public string HomebrewTapRepo { get; set; } = "";
        public string ScoopBucketRepo { get; set; } = "";
        public string WingetPkgsRepo { get; set; } = "";
        public string NugetSource { get; set; } = "";
    }

    /// <summary>Build inputs: artifacts, frameworks, and runtimes.</summary>
    public sealed class BuildInfo
    {
        public List<ArtifactInfo> Artifacts { get; set; } = new List<ArtifactInfo>();
        public List<string> Frameworks { get; set; } = new List<string>();
        public List<string> Runtimes { get; set; } = new List<string>();
    }

    /// <summary>A single buildable artifact (a csproj plus how it is meant to run).</summary>
    public sealed class ArtifactInfo
    {
        /// <summary>Short id referenced by channels ("agent", "tui").</summary>
        public string Id { get; set; } = "";

        /// <summary>Path to the csproj, relative to the repo root.</summary>
        public string Csproj { get; set; } = "";

        /// <summary>Base name of the published executable (no extension), e.g. "Armor.Agent".</summary>
        public string ExeName { get; set; } = "";

        /// <summary>How the artifact runs ("Daemon", "Server", ...). Informational for now.</summary>
        public string Kind { get; set; } = "";
    }

    /// <summary>Configuration for one packaging channel.</summary>
    public sealed class ChannelInfo
    {
        /// <summary>When false, the channel is skipped.</summary>
        public bool Enabled { get; set; }

        /// <summary>Which artifact id this channel packages.</summary>
        public string Artifact { get; set; } = "";

        /// <summary>
        /// Runtimes to build for this channel. When empty, the driver derives them from
        /// <see cref="BuildInfo.Runtimes"/> filtered to the channel's OS family.
        /// </summary>
        public List<string> Runtimes { get; set; } = new List<string>();

        /// <summary>Free-form channel-specific options.</summary>
        public Dictionary<string, JsonElement> Options { get; set; } = new Dictionary<string, JsonElement>();
    }

    /// <summary>Per-OS signing configuration.</summary>
    public sealed class SigningInfo
    {
        public SigningTarget MacOs { get; set; } = new SigningTarget();
        public SigningTarget Windows { get; set; } = new SigningTarget();
        public SigningTarget Linux { get; set; } = new SigningTarget();
    }

    /// <summary>Signing settings for a single OS.</summary>
    public sealed class SigningTarget
    {
        /// <summary>Name of the GitHub/CI secret holding the signing identity (cert, key, ...).</summary>
        public string VaultRef { get; set; } = "";

        /// <summary>Name of the secret holding a password/passphrase for the identity, if any.</summary>
        public string PasswordRef { get; set; } = "";

        /// <summary>Name of the secret holding a notarization profile/API key (macOS).</summary>
        public string NotarizeProfileRef { get; set; } = "";

        /// <summary>Whether to notarize/timestamp after signing.</summary>
        public bool Notarize { get; set; }

        /// <summary>Whether signing is fully configured; when false, channels build unsigned.</summary>
        public bool IsConfigured { get; set; }
    }

    /// <summary>Where release notes are sourced from.</summary>
    public sealed class ReleaseNotesInfo
    {
        /// <summary>File to read notes from (e.g. "CHANGELOG.md").</summary>
        public string Source { get; set; } = "";

        /// <summary>Heading pattern that delimits a version's section (e.g. "## {version}").</summary>
        public string Section { get; set; } = "";
    }
}
