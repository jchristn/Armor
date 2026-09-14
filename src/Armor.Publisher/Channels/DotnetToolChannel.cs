using System;
using System.Collections.Generic;
using System.IO;

namespace Armor.Publisher.Channels
{
    /// <summary>
    /// .NET global-tool channel. Packs the configured artifact (the TUI) as a dotnet tool package
    /// so it installs via `dotnet tool install -g`. The workflow pushes the .nupkg to NuGet.
    /// </summary>
    public sealed class DotnetToolChannel : IChannel
    {
        /// <inheritdoc/>
        public string Id => "dotnettool";
        /// <inheritdoc/>
        public string DisplayName => ".NET global tool (NuGet)";
        /// <inheritdoc/>
        public HostOs RequiredHost => HostOs.Any;

        /// <inheritdoc/>
        public IReadOnlyList<string> Build(ChannelContext context)
        {
            string csproj = Path.GetFullPath(Path.Combine(context.RepoRoot, context.Artifact.Csproj));
            if (!File.Exists(csproj))
                throw new FileNotFoundException($"Artifact csproj not found at '{csproj}'.");

            string packageId = context.Option("packageId", context.Config.Project.PackageId + ".Tui");
            string command = context.Option("toolCommandName", context.Config.Project.Name.ToLowerInvariant());
            string outDir = context.OutputDir;

            Console.WriteLine($"[dotnettool] packing {packageId} (command '{command}')");

            // Restore separately: pack's PackAsTool/PackageId are pack-only. Passing them on a
            // combined restore+pack makes NuGet apply PackageId to the referenced projects too
            // ("Ambiguous project name"). Restore first, then pack with --no-restore.
            ProcessRunner.Run("dotnet", new[]
            {
                "restore", csproj, $"-p:TargetFrameworks={context.Framework}"
            }, context.RepoRoot);

            ProcessRunner.Run("dotnet", new[]
            {
                "pack", csproj,
                "-c", "Release",
                "--no-restore",
                // dotnet pack has no --framework switch; pin the single TFM via a property instead.
                $"-p:TargetFrameworks={context.Framework}",
                "-o", outDir,
                "-p:PackAsTool=true",
                $"-p:ToolCommandName={command}",
                $"-p:PackageId={packageId}",
                $"-p:Version={context.Version}",
                $"-p:PackageProjectUrl={context.Config.Project.Homepage}",
                $"-p:PackageLicenseExpression={context.Config.Project.License}"
            }, context.RepoRoot);

            string nupkg = Path.Combine(outDir, $"{packageId}.{context.Version}.nupkg");
            if (File.Exists(nupkg)) context.EmitChecksum(nupkg);
            return new[] { nupkg };
        }
    }
}
