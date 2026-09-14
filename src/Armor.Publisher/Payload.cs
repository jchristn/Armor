using System.Collections.Generic;
using System.IO;

namespace Armor.Publisher
{
    /// <summary>
    /// Publishes an artifact for a single runtime identifier and returns the output directory.
    /// This is the shared first step every OS channel builds on: a self-contained, single-file
    /// publish of the target csproj.
    /// </summary>
    public static class Payload
    {
        /// <summary>
        /// Runs <c>dotnet publish</c> (Release, self-contained, single-file) for
        /// <paramref name="artifact"/> targeting <paramref name="rid"/>, and returns the absolute
        /// path to the publish output directory.
        /// </summary>
        /// <param name="repoRoot">Repo root; artifact csproj paths are relative to it.</param>
        /// <param name="artifact">Artifact to publish.</param>
        /// <param name="rid">Runtime identifier, e.g. "win-x64".</param>
        /// <param name="framework">Target framework moniker, e.g. "net10.0".</param>
        /// <param name="stagingRoot">Directory under which per-rid publish folders are created.</param>
        public static string Publish(string repoRoot, ArtifactInfo artifact, string rid, string framework, string stagingRoot)
        {
            string csproj = Path.GetFullPath(Path.Combine(repoRoot, artifact.Csproj));
            if (!File.Exists(csproj))
                throw new FileNotFoundException($"Artifact '{artifact.Id}' csproj not found at '{csproj}'.");

            string outDir = Path.Combine(stagingRoot, $"{artifact.Id}-{rid}");
            Directory.CreateDirectory(outDir);

            List<string> args = new List<string>
            {
                "publish", csproj,
                "-c", "Release",
                "-r", rid,
                "-f", framework,
                "--self-contained", "true",
                "-p:PublishSingleFile=true",
                "-p:IncludeNativeLibrariesForSelfExtract=true",
                "-p:DebugType=none",
                "-o", outDir
            };

            ProcessRunner.Run("dotnet", args, repoRoot);
            return outDir;
        }
    }
}
