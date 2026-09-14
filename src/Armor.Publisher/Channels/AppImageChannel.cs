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
                string payloadDir = context.PublishPayload(rid);

                string appDir = Path.Combine(context.StagingDir, $"{appName}-{rid}.AppDir");
                if (Directory.Exists(appDir)) Directory.Delete(appDir, true);
                string binDir = Path.Combine(appDir, "usr", "bin");
                Directory.CreateDirectory(binDir);
                FileSystemUtil.CopyTree(payloadDir, binDir);

                // .desktop and icon are required by appimagetool.
                string desktop = Path.Combine(appDir, $"{appName.ToLowerInvariant()}.desktop");
                File.WriteAllText(desktop, DesktopEntry(context, appName));
                // A tiny placeholder icon keeps appimagetool happy until real art is added.
                File.WriteAllText(Path.Combine(appDir, $"{appName.ToLowerInvariant()}.png"), "");

                string appRun = Path.Combine(appDir, "AppRun");
                File.WriteAllText(appRun, $"#!/bin/sh\nHERE=$(dirname \"$(readlink -f \"$0\")\")\nexec \"$HERE/usr/bin/{context.ExeName}\" \"$@\"\n");
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
