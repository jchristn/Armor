using System;
using System.Collections.Generic;
using System.IO;

namespace Armor.Publisher.Channels
{
    /// <summary>
    /// Windows installer channel. Publishes the agent for each Windows rid, renders the Inno Setup
    /// script template, and runs the Inno compiler (iscc) to produce a setup .exe per architecture.
    /// Requires Inno Setup 6 (iscc.exe) on PATH or at its default install location.
    /// </summary>
    public sealed class InnoChannel : IChannel
    {
        /// <inheritdoc/>
        public string Id => "inno";

        /// <inheritdoc/>
        public string DisplayName => "Inno Setup (Windows)";

        /// <inheritdoc/>
        public HostOs RequiredHost => HostOs.Windows;

        /// <inheritdoc/>
        public IReadOnlyList<string> Build(ChannelContext context)
        {
            string iscc = ResolveIscc();
            string template = ReadTemplate();
            // Stage the license once; iscc reads LicenseFile relative to the .iss location (StagingDir).
            string licenseRel = StageLicense(context);
            List<string> outputs = new List<string>();

            foreach (string rid in context.Runtimes)
            {
                Console.WriteLine($"[inno] publishing {context.Artifact.Id} for {rid}");
                string payloadDir = context.PublishCombinedPayload(rid);

                // Inno Setup 6.3+ architecture identifiers: "arm64" and "x64compatible"
                // (the older bare "x64" is rejected by current compilers).
                string arch = rid.EndsWith("arm64", StringComparison.OrdinalIgnoreCase) ? "arm64" : "x64compatible";
                string setupBaseName = $"{context.Config.Project.Name}-{context.Version}-{rid}-setup";

                // Render the .iss for this rid into the staging dir. iscc reads files relative to
                // the script's own location, so keep it beside the payload it references.
                string issPath = Path.Combine(context.StagingDir, $"{context.Config.Project.Name}-{rid}.iss");
                string rendered = template
                    .Replace("{{AppName}}", context.Config.Project.DisplayName)
                    .Replace("{{AppVersion}}", context.Version)
                    .Replace("{{AppPublisher}}", "Joel Christner")
                    .Replace("{{AppUrl}}", context.Config.Project.Repo)
                    .Replace("{{ArchitecturesAllowed}}", arch)
                    .Replace("{{ArchitecturesInstallIn64BitMode}}", arch)
                    .Replace("{{PayloadDir}}", payloadDir)
                    .Replace("{{LicenseFile}}", licenseRel)
                    .Replace("{{ExtraIcons}}", BuildExtraIcons(context))
                    .Replace("{{ExeName}}", context.ExeName + ".exe")
                    .Replace("{{OutputDir}}", context.OutputDir)
                    .Replace("{{OutputBaseName}}", setupBaseName);
                File.WriteAllText(issPath, rendered);

                Console.WriteLine($"[inno] compiling {setupBaseName}.exe");
                ProcessRunner.Run(iscc, new[] { issPath }, context.StagingDir);

                string produced = Path.Combine(context.OutputDir, setupBaseName + ".exe");
                Signing.WindowsAuthenticode(produced, context.Config.Signing.Windows);
                context.EmitChecksum(produced);
                outputs.Add(produced);
            }

            return outputs;
        }

        /// <summary>
        /// Builds one [Icons] line per bundled CLI so the extra executables (installed into {app}
        /// by the combined payload) get their own Start-menu shortcuts. Empty when nothing extra
        /// is bundled.
        /// </summary>
        private static string BuildExtraIcons(ChannelContext context)
        {
            string icons = "";
            foreach (string id in context.IncludedArtifacts)
            {
                string exe = context.ExeNameOf(id) + ".exe";
                string label = context.Config.Project.DisplayName + " " + id.ToUpperInvariant();
                icons += $"Name: \"{{group}}\\{label}\"; Filename: \"{{app}}\\{exe}\"\n";
            }
            return icons;
        }

        /// <summary>
        /// Copies the project's license into the staging dir as LICENSE.txt and returns the file
        /// name to reference from the .iss (relative to the script's own location). Returns an empty
        /// string when no license file exists, which leaves the Inno license page disabled.
        /// </summary>
        private static string StageLicense(ChannelContext context)
        {
            string? src = context.LicenseFile();
            if (src == null)
            {
                Console.WriteLine("[inno] WARNING: no license file found at repo root; the installer will not show a license page.");
                return "";
            }

            string dst = Path.Combine(context.StagingDir, "LICENSE.txt");
            File.Copy(src, dst, overwrite: true);
            return "LICENSE.txt";
        }

        /// <summary>Finds iscc.exe on PATH or at the standard Inno Setup 6 install locations.</summary>
        private static string ResolveIscc()
        {
            string[] candidates =
            {
                Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86), "Inno Setup 6", "ISCC.exe"),
                Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "Inno Setup 6", "ISCC.exe")
            };
            foreach (string c in candidates)
                if (File.Exists(c)) return c;

            // Fall back to PATH resolution; ProcessRunner surfaces a clear error if it is missing.
            return "iscc";
        }

        /// <summary>Reads the bundled Inno Setup script template.</summary>
        private static string ReadTemplate()
        {
            string path = Path.Combine(AppContext.BaseDirectory, "Templates", "armor.iss");
            if (!File.Exists(path))
                throw new FileNotFoundException($"Inno template not found at '{path}'. Was Templates/armor.iss copied to output?");
            return File.ReadAllText(path);
        }
    }
}
