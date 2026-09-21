using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using Armor.Publisher;
using Armor.Publisher.Channels;

namespace Test.Publisher
{
    /// <summary>Unit tests for the Publisher's pure logic (no external packagers required).</summary>
    public class PublisherTests
    {
        [Fact]
        public void Sha256_matches_known_vector()
        {
            string tmp = Path.GetTempFileName();
            try
            {
                File.WriteAllText(tmp, "abc");
                // SHA-256("abc")
                Assert.Equal("ba7816bf8f01cfea414140de5dae2223b00361a396177a9cb410ff61f20015ad",
                    Checksums.Sha256(tmp));
            }
            finally { File.Delete(tmp); }
        }

        [Fact]
        public void WriteSidecar_creates_hash_file()
        {
            string tmp = Path.GetTempFileName();
            try
            {
                File.WriteAllText(tmp, "hello");
                string hash = Checksums.WriteSidecar(tmp);
                Assert.True(File.Exists(tmp + ".sha256"));
                Assert.Contains(hash, File.ReadAllText(tmp + ".sha256"));
                Assert.Contains(Path.GetFileName(tmp), File.ReadAllText(tmp + ".sha256"));
            }
            finally
            {
                File.Delete(tmp);
                if (File.Exists(tmp + ".sha256")) File.Delete(tmp + ".sha256");
            }
        }

        [Fact]
        public void Config_loads_and_finds_artifacts_and_channels()
        {
            string json = @"{
              ""schemaVersion"": 2,
              ""project"": { ""name"": ""Armor"", ""packageId"": ""JoelChristner.Armor"" },
              ""build"": {
                ""artifacts"": [ { ""id"": ""agent"", ""csproj"": ""src/Armor.Agent/Armor.Agent.csproj"", ""exeName"": ""Armor.Agent"" } ],
                ""frameworks"": [ ""net10.0"" ],
                ""runtimes"": [ ""win-x64"", ""linux-x64"" ]
              },
              ""destinations"": { ""githubRepo"": ""jchristn/armor"" },
              ""channels"": { ""inno"": { ""enabled"": true, ""artifact"": ""agent"" } }
            }";
            string tmp = Path.GetTempFileName();
            try
            {
                File.WriteAllText(tmp, json);
                PublisherConfig cfg = PublisherConfig.Load(tmp);

                Assert.Equal(2, cfg.SchemaVersion);
                Assert.Equal("JoelChristner.Armor", cfg.Project.PackageId);
                Assert.Equal("jchristn/armor", cfg.Destinations.GithubRepo);
                Assert.NotNull(cfg.FindArtifact("agent"));
                Assert.Equal("Armor.Agent", cfg.FindArtifact("agent")!.ExeName);
                Assert.Null(cfg.FindArtifact("nope"));
                Assert.True(cfg.Channels["inno"].Enabled);
            }
            finally { File.Delete(tmp); }
        }

        [Fact]
        public void ReleaseAssetUrl_uses_repo_and_version_tag()
        {
            ChannelContext ctx = new ChannelContext
            {
                Version = "1.2.3",
                Config = new PublisherConfig { Destinations = new DestinationsInfo { GithubRepo = "jchristn/armor" } }
            };
            Assert.Equal(
                "https://github.com/jchristn/armor/releases/download/v1.2.3/Armor-setup.exe",
                ctx.ReleaseAssetUrl("Armor-setup.exe"));
        }

        [Fact]
        public void Option_reads_channel_options_with_fallback()
        {
            ChannelInfo channel = new ChannelInfo();
            using JsonDocument doc = JsonDocument.Parse("{ \"caskName\": \"armor\" }");
            foreach (JsonProperty p in doc.RootElement.EnumerateObject())
                channel.Options[p.Name] = p.Value.Clone();

            ChannelContext ctx = new ChannelContext { Channel = channel };
            Assert.Equal("armor", ctx.Option("caskName", "fallback"));
            Assert.Equal("fallback", ctx.Option("missing", "fallback"));
        }

        [Fact]
        public void ExeName_falls_back_to_artifact_id()
        {
            ChannelContext withName = new ChannelContext { Artifact = new ArtifactInfo { Id = "agent", ExeName = "Armor.Agent" } };
            ChannelContext without = new ChannelContext { Artifact = new ArtifactInfo { Id = "agent" } };
            Assert.Equal("Armor.Agent", withName.ExeName);
            Assert.Equal("agent", without.ExeName);
        }

        [Theory]
        [InlineData("inno", typeof(InnoChannel), HostOs.Windows)]
        [InlineData("dmg", typeof(DmgChannel), HostOs.MacOs)]
        [InlineData("debrpm", typeof(DebRpmChannel), HostOs.Linux)]
        [InlineData("appimage", typeof(AppImageChannel), HostOs.Linux)]
        [InlineData("brewcask", typeof(BrewCaskChannel), HostOs.Any)]
        [InlineData("dotnettool", typeof(DotnetToolChannel), HostOs.Any)]
        public void Registry_resolves_channels(string id, System.Type type, HostOs host)
        {
            IChannel? c = ChannelRegistry.Resolve(id);
            Assert.NotNull(c);
            Assert.IsType(type, c);
            Assert.Equal(host, c!.RequiredHost);
        }

        [Fact]
        public void Registry_returns_null_for_unknown()
        {
            Assert.Null(ChannelRegistry.Resolve("does-not-exist"));
        }

        [Fact]
        public void LicenseFile_finds_license_at_repo_root()
        {
            string repo = Directory.CreateTempSubdirectory("armor-lic").FullName;
            try
            {
                string license = Path.Combine(repo, "LICENSE.md");
                File.WriteAllText(license, "MIT License ...");
                ChannelContext ctx = new ChannelContext { RepoRoot = repo };
                Assert.Equal(license, ctx.LicenseFile());
            }
            finally { Directory.Delete(repo, true); }
        }

        [Fact]
        public void Channel_include_parses_and_defaults_empty()
        {
            string json = @"{
              ""schemaVersion"": 2,
              ""project"": { ""name"": ""Armor"" },
              ""build"": { ""artifacts"": [ { ""id"": ""agent"" }, { ""id"": ""tui"" } ] },
              ""channels"": {
                ""inno"": { ""enabled"": true, ""artifact"": ""agent"", ""include"": [ ""tui"" ] },
                ""winget"": { ""enabled"": true, ""artifact"": ""agent"" }
              }
            }";
            string tmp = Path.GetTempFileName();
            try
            {
                File.WriteAllText(tmp, json);
                PublisherConfig cfg = PublisherConfig.Load(tmp);
                Assert.Equal(new[] { "tui" }, cfg.Channels["inno"].Include);
                Assert.Empty(cfg.Channels["winget"].Include);
            }
            finally { File.Delete(tmp); }
        }

        [Fact]
        public void ExeNameOf_resolves_included_artifact_exe()
        {
            PublisherConfig cfg = new PublisherConfig();
            cfg.Build.Artifacts.Add(new ArtifactInfo { Id = "tui", ExeName = "Armor.Tui" });
            cfg.Build.Artifacts.Add(new ArtifactInfo { Id = "cli" });
            ChannelContext ctx = new ChannelContext { Config = cfg };
            Assert.Equal("Armor.Tui", ctx.ExeNameOf("tui"));
            Assert.Equal("cli", ctx.ExeNameOf("cli"));       // falls back to id when exeName unset
            Assert.Equal("nope", ctx.ExeNameOf("nope"));     // unknown id echoes back
        }

        [Fact]
        public void LicenseFile_returns_null_when_absent()
        {
            string repo = Directory.CreateTempSubdirectory("armor-nolic").FullName;
            try
            {
                ChannelContext ctx = new ChannelContext { RepoRoot = repo };
                Assert.Null(ctx.LicenseFile());
            }
            finally { Directory.Delete(repo, true); }
        }
    }
}
