using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text.RegularExpressions;
using Armor.Publisher.Channels;

namespace Armor.Publisher
{
    /// <summary>
    /// Entry point for the Armor publisher.
    ///
    /// Usage:
    ///   armor-publish --channel &lt;id&gt; [--rid &lt;rid&gt;] [--version &lt;v&gt;]
    ///                 [--config publisher.json] [--output ./artifacts] [--list]
    ///
    /// The tool reads publisher.json, publishes the channel's artifact (self-contained, single-file)
    /// for the resolved runtimes, and produces installer packages in the output directory.
    /// </summary>
    public static class Program
    {
        /// <summary>Process entry point. Returns 0 on success, non-zero on failure.</summary>
        public static int Main(string[] args)
        {
            try
            {
                Dictionary<string, string> opts = ParseArgs(args);

                if (opts.ContainsKey("help") || args.Length == 0)
                {
                    PrintUsage();
                    return 0;
                }

                string configPath = Path.GetFullPath(opts.GetValueOrDefault("config", "publisher.json"));
                if (!File.Exists(configPath))
                {
                    Console.Error.WriteLine($"Config not found: {configPath}");
                    return 2;
                }

                PublisherConfig config = PublisherConfig.Load(configPath);
                string repoRoot = Path.GetDirectoryName(configPath)!;

                if (opts.ContainsKey("print-version"))
                {
                    // Prints only the resolved version to stdout so scripts can name output folders.
                    Console.WriteLine(opts.GetValueOrDefault("version", ReadVersion(repoRoot)));
                    return 0;
                }

                if (opts.ContainsKey("list"))
                {
                    ListChannels(config);
                    return 0;
                }

                if (!opts.TryGetValue("channel", out string? channelId))
                {
                    Console.Error.WriteLine("Missing required --channel. Use --list to see channels.");
                    return 2;
                }

                if (!config.Channels.TryGetValue(channelId, out ChannelInfo? channelCfg))
                {
                    Console.Error.WriteLine($"Channel '{channelId}' is not declared in publisher.json.");
                    return 2;
                }
                if (!channelCfg.Enabled)
                {
                    Console.Error.WriteLine($"Channel '{channelId}' is disabled in publisher.json.");
                    return 2;
                }

                IChannel? channel = ChannelRegistry.Resolve(channelId);
                if (channel == null)
                {
                    Console.Error.WriteLine($"No implementation registered for channel '{channelId}'.");
                    return 2;
                }

                EnsureHost(channel);

                ArtifactInfo? artifact = config.FindArtifact(channelCfg.Artifact);
                if (artifact == null)
                {
                    Console.Error.WriteLine($"Channel '{channelId}' references unknown artifact '{channelCfg.Artifact}'.");
                    return 2;
                }

                string version = opts.GetValueOrDefault("version", ReadVersion(repoRoot));
                string framework = opts.GetValueOrDefault("framework", config.Build.Frameworks.FirstOrDefault() ?? "net10.0");
                IReadOnlyList<string> rids = ResolveRuntimes(opts, channelId, channelCfg, config);
                if (rids.Count == 0)
                {
                    Console.Error.WriteLine($"No runtimes resolved for channel '{channelId}'.");
                    return 2;
                }

                string outputDir = Path.GetFullPath(opts.GetValueOrDefault("output", Path.Combine(repoRoot, "artifacts")));
                string stagingDir = Path.Combine(outputDir, ".staging", channelId);
                Directory.CreateDirectory(outputDir);
                Directory.CreateDirectory(stagingDir);

                ChannelContext ctx = new ChannelContext
                {
                    RepoRoot = repoRoot,
                    Config = config,
                    Channel = channelCfg,
                    Artifact = artifact,
                    Version = version,
                    Runtimes = rids,
                    Framework = framework,
                    OutputDir = outputDir,
                    StagingDir = stagingDir,
                    ReleaseNotes = ReadReleaseNotes(repoRoot, config, version)
                };

                Console.WriteLine($"== {channel.DisplayName} ==");
                Console.WriteLine($"   artifact : {artifact.Id} ({artifact.Csproj})");
                Console.WriteLine($"   version  : {version}");
                Console.WriteLine($"   runtimes : {string.Join(", ", rids)}");
                Console.WriteLine($"   output   : {outputDir}");
                Console.WriteLine();

                IReadOnlyList<string> produced = channel.Build(ctx);

                Console.WriteLine();
                Console.WriteLine($"Done. {produced.Count} artifact(s):");
                foreach (string p in produced) Console.WriteLine("  " + p);
                return 0;
            }
            catch (ProcessFailedException ex)
            {
                Console.Error.WriteLine("ERROR: " + ex.Message);
                // Always return a positive code; negatives read as "no error" to cmd's errorlevel.
                return ex.ExitCode > 0 ? ex.ExitCode : 1;
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine("ERROR: " + ex.Message);
                return 1;
            }
        }

        /// <summary>Parses --key value / --flag arguments into a dictionary.</summary>
        private static Dictionary<string, string> ParseArgs(string[] args)
        {
            Dictionary<string, string> result = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            for (int i = 0; i < args.Length; i++)
            {
                string a = args[i];
                if (!a.StartsWith("--")) continue;
                string key = a.Substring(2);
                if (i + 1 < args.Length && !args[i + 1].StartsWith("--"))
                {
                    result[key] = args[i + 1];
                    i++;
                }
                else
                {
                    result[key] = "true";
                }
            }
            return result;
        }

        /// <summary>Verifies the current OS matches what the channel requires.</summary>
        private static void EnsureHost(IChannel channel)
        {
            HostOs current =
                RuntimeInformation.IsOSPlatform(OSPlatform.Windows) ? HostOs.Windows :
                RuntimeInformation.IsOSPlatform(OSPlatform.OSX) ? HostOs.MacOs :
                RuntimeInformation.IsOSPlatform(OSPlatform.Linux) ? HostOs.Linux : HostOs.Any;

            if (channel.RequiredHost != HostOs.Any && channel.RequiredHost != current)
                throw new PlatformNotSupportedException(
                    $"Channel '{channel.Id}' must run on {channel.RequiredHost}, but this is {current}. " +
                    "Run it on the matching CI runner (see .github/workflows/release.yml).");
        }

        /// <summary>Resolves the runtimes to build: --rid override, channel list, or OS-filtered build list.</summary>
        private static IReadOnlyList<string> ResolveRuntimes(
            Dictionary<string, string> opts, string channelId, ChannelInfo channelCfg, PublisherConfig config)
        {
            if (opts.TryGetValue("rid", out string? rid)) return new[] { rid };
            if (channelCfg.Runtimes.Count > 0) return channelCfg.Runtimes;

            string[] prefixes = OsPrefixesForChannel(channelId);
            return config.Build.Runtimes
                .Where(r => prefixes.Any(p => r.StartsWith(p, StringComparison.OrdinalIgnoreCase)))
                .ToList();
        }

        /// <summary>The rid OS-family prefixes a channel builds for when it declares no runtimes.</summary>
        private static string[] OsPrefixesForChannel(string channelId)
        {
            switch (channelId.ToLowerInvariant())
            {
                case "inno":
                case "scoop":
                case "choco":
                case "winget": return new[] { "win-" };
                case "dmg":
                case "brewcask": return new[] { "osx-" };
                case "debrpm":
                case "appimage":
                case "aptyum":
                case "snap":
                case "flatpak":
                case "docker":
                case "helm": return new[] { "linux-" };
                default: return new[] { "win-", "osx-", "linux-" };
            }
        }

        /// <summary>Reads &lt;Version&gt; from src/Directory.Build.props, defaulting to 0.0.0.</summary>
        private static string ReadVersion(string repoRoot)
        {
            string props = Path.Combine(repoRoot, "src", "Directory.Build.props");
            if (File.Exists(props))
            {
                Match m = Regex.Match(File.ReadAllText(props), "<Version>([^<]+)</Version>");
                if (m.Success) return m.Groups[1].Value.Trim();
            }
            return "0.0.0";
        }

        /// <summary>Extracts the release-notes section for a version from the configured source file.</summary>
        private static string ReadReleaseNotes(string repoRoot, PublisherConfig config, string version)
        {
            if (string.IsNullOrEmpty(config.ReleaseNotes.Source) || string.IsNullOrEmpty(config.ReleaseNotes.Section))
                return "";
            string path = Path.Combine(repoRoot, config.ReleaseNotes.Source);
            if (!File.Exists(path)) return "";

            // Turn "## {version}" into a heading matcher and capture until the next heading of the
            // same level.
            string headingLiteral = Regex.Escape(config.ReleaseNotes.Section).Replace("\\{version}", Regex.Escape(version));
            string[] lines = File.ReadAllLines(path);
            List<string> captured = new List<string>();
            bool inSection = false;
            Regex start = new Regex("^" + headingLiteral + "\\b");
            for (int i = 0; i < lines.Length; i++)
            {
                if (!inSection)
                {
                    if (start.IsMatch(lines[i])) inSection = true;
                    continue;
                }
                if (lines[i].StartsWith("## ")) break; // next version section
                captured.Add(lines[i]);
            }
            return string.Join("\n", captured).Trim();
        }

        private static void ListChannels(PublisherConfig config)
        {
            Console.WriteLine("Channels (declared in publisher.json):");
            foreach (string id in ChannelRegistry.KnownIds)
            {
                config.Channels.TryGetValue(id, out ChannelInfo? c);
                IChannel? impl = ChannelRegistry.Resolve(id);
                string status = c == null ? "not declared" : (c.Enabled ? "enabled" : "disabled");
                string implState = impl is StubChannel ? "stub" : "implemented";
                Console.WriteLine($"  {id,-10} {status,-12} {implState,-12} -> {impl?.DisplayName}");
            }
        }

        private static void PrintUsage()
        {
            Console.WriteLine("Armor publisher");
            Console.WriteLine();
            Console.WriteLine("  armor-publish --channel <id> [options]");
            Console.WriteLine();
            Console.WriteLine("Options:");
            Console.WriteLine("  --channel <id>     Channel to build (inno, dmg, debrpm, homebrew, docker, helm, aptyum)");
            Console.WriteLine("  --rid <rid>        Build a single runtime instead of the channel default");
            Console.WriteLine("  --version <v>      Override the version (default: <Version> in Directory.Build.props)");
            Console.WriteLine("  --framework <tfm>  Target framework (default: first in build.frameworks)");
            Console.WriteLine("  --config <path>    Path to publisher.json (default: ./publisher.json)");
            Console.WriteLine("  --output <dir>     Output directory (default: ./artifacts)");
            Console.WriteLine("  --list             List channels and their status");
            Console.WriteLine("  --print-version    Print the resolved version and exit");
            Console.WriteLine("  --help             Show this help");
        }
    }
}
