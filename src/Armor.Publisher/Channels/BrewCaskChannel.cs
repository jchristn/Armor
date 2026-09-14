using System;
using System.Collections.Generic;
using System.IO;

namespace Armor.Publisher.Channels
{
    /// <summary>
    /// Homebrew cask generator. Emits a Ruby cask referencing the notarized .pkg assets (per arch)
    /// by URL + SHA-256. The workflow pushes it to the tap repo so `brew install --cask` works.
    /// </summary>
    public sealed class BrewCaskChannel : IChannel
    {
        /// <inheritdoc/>
        public string Id => "brewcask";
        /// <inheritdoc/>
        public string DisplayName => "Homebrew cask (macOS)";
        /// <inheritdoc/>
        public HostOs RequiredHost => HostOs.Any;

        /// <inheritdoc/>
        public IReadOnlyList<string> Build(ChannelContext context)
        {
            string name = context.Config.Project.Name;
            string cask = context.Option("caskName", name.ToLowerInvariant());

            string armPkg = Require(context, "osx-arm64");
            string intelPkg = Require(context, "osx-x64");
            string armSha = Checksums.Sha256(armPkg);
            string intelSha = Checksums.Sha256(intelPkg);

            string ruby =
                $"cask \"{cask}\" do\n" +
                "  arch arm: \"arm64\", intel: \"x64\"\n\n" +
                $"  version \"{context.Version}\"\n\n" +
                "  on_arm do\n" +
                $"    sha256 \"{armSha}\"\n" +
                "  end\n" +
                "  on_intel do\n" +
                $"    sha256 \"{intelSha}\"\n" +
                "  end\n\n" +
                $"  url \"{context.Config.Project.Repo}/releases/download/v#{{version}}/{name}-#{{version}}-osx-#{{arch}}.pkg\"\n" +
                $"  name \"{context.Config.Project.DisplayName}\"\n" +
                $"  desc \"{context.Config.Project.Description}\"\n" +
                $"  homepage \"{context.Config.Project.Homepage}\"\n\n" +
                $"  pkg \"{name}-#{{version}}-osx-#{{arch}}.pkg\"\n\n" +
                $"  uninstall pkgutil: \"com.joelchristner.{cask}\"\n" +
                "end\n";

            string dir = Path.Combine(context.OutputDir, "manifests", "brewcask");
            Directory.CreateDirectory(dir);
            string outPath = Path.Combine(dir, cask + ".rb");
            File.WriteAllText(outPath, ruby);
            Console.WriteLine($"[brewcask] wrote {outPath} (push to Casks/ in {context.Config.Destinations.HomebrewTapRepo})");
            return new[] { outPath };
        }

        private static string Require(ChannelContext ctx, string rid)
        {
            string pattern = $"{ctx.Config.Project.Name}-{ctx.Version}-{rid}.pkg";
            string? path = ctx.FindAsset(pattern);
            if (path == null)
                throw new FileNotFoundException(
                    $"Expected macOS package '{pattern}' in {ctx.OutputDir}. Run the 'dmg' channel first " +
                    "(in CI, download the dmg artifact into the output dir before this channel).");
            return path;
        }
    }
}
