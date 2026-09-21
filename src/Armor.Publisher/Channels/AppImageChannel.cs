using System;
using System.Collections.Generic;
using System.IO;

namespace Armor.Publisher.Channels
{
    /// <summary>
    /// Linux AppImage channel. Publishes the agent per linux rid, assembles an AppDir (AppRun,
    /// .desktop, icon, payload), and runs appimagetool to produce a distro-agnostic single file.
    /// Requires Linux with appimagetool available (the workflow downloads it).
    /// </summary>
    public sealed class AppImageChannel : IChannel
    {
        /// <inheritdoc/>
        public string Id => "appimage";

        /// <inheritdoc/>
        public string DisplayName => "AppImage (Linux)";

        /// <inheritdoc/>
        public HostOs RequiredHost => HostOs.Linux;

        /// <inheritdoc/>
        public IReadOnlyList<string> Build(ChannelContext context)
        {
            List<string> outputs = new List<string>();
            string appName = context.Config.Project.Name;

            foreach (string rid in context.Runtimes)
            {
                Console.WriteLine($"[appimage] publishing {context.Artifact.Id} for {rid}");
                string payloadDir = context.PublishCombinedPayload(rid);

                string appDir = Path.Combine(context.StagingDir, $"{appName}-{rid}.AppDir");
                if (Directory.Exists(appDir)) Directory.Delete(appDir, true);
                string binDir = Path.Combine(appDir, "usr", "bin");
                Directory.CreateDirectory(binDir);
                FileSystemUtil.CopyTree(payloadDir, binDir);

                // An AppImage is a portable executable with no install step, so it can't demand
                // acceptance; ship the license inside the AppDir so it travels with the binary.
                string? licenseSrc = context.LicenseFile();
                if (licenseSrc != null)
                    File.Copy(licenseSrc, Path.Combine(binDir, Path.GetFileName(licenseSrc)), overwrite: true);
                else
                    Console.WriteLine("[appimage] WARNING: no license file found at repo root; the AppImage will not include a license.");

                // .desktop and icon are required by appimagetool.
                string desktop = Path.Combine(appDir, $"{appName.ToLowerInvariant()}.desktop");
                File.WriteAllText(desktop, DesktopEntry(context, appName));
                // A tiny placeholder icon keeps appimagetool happy until real art is added.
                File.WriteAllText(Path.Combine(appDir, $"{appName.ToLowerInvariant()}.png"), "");

                string appRun = Path.Combine(appDir, "AppRun");
                File.WriteAllText(appRun, AppRunScript(context));
                ProcessRunner.Run("chmod", new[] { "+x", appRun });

                string archTag = rid.EndsWith("arm64", StringComparison.OrdinalIgnoreCase) ? "aarch64" : "x86_64";
                string outName = $"{appName}-{context.Version}-{archTag}.AppImage";
                string outPath = Path.Combine(context.OutputDir, outName);
                if (File.Exists(outPath)) File.Delete(outPath);

                Console.WriteLine($"[appimage] building {outName}");
                // ARCH env tells appimagetool the target architecture for cross builds.
                Environment.SetEnvironmentVariable("ARCH", archTag);
                ProcessRunner.Run("appimagetool", new[] { appDir, outPath });

                context.EmitChecksum(outPath);
                outputs.Add(outPath);
            }

            return outputs;
        }

        /// <summary>
        /// Builds the AppRun entry point. An AppImage has a single entry, so bundled CLIs are reached
        /// via a leading subcommand equal to their artifact id (e.g. "./Armor-*.AppImage tui ..."),
        /// which shifts the argument and execs that binary. With no subcommand it runs the agent.
        /// </summary>
        private static string AppRunScript(ChannelContext context)
        {
            string sh = "#!/bin/sh\nHERE=$(dirname \"$(readlink -f \"$0\")\")\n";
            foreach (string id in context.IncludedArtifacts)
                sh += $"if [ \"$1\" = \"{id.ToLowerInvariant()}\" ]; then shift; exec \"$HERE/usr/bin/{context.ExeNameOf(id)}\" \"$@\"; fi\n";
            sh += $"exec \"$HERE/usr/bin/{context.ExeName}\" \"$@\"\n";
            return sh;
        }

        private static string DesktopEntry(ChannelContext context, string appName) =>
            "[Desktop Entry]\n" +
            $"Name={context.Config.Project.DisplayName}\n" +
            $"Exec={context.ExeName}\n" +
            $"Icon={appName.ToLowerInvariant()}\n" +
            "Type=Application\n" +
            "Categories=Utility;\n" +
            "Terminal=false\n";
    }
}
