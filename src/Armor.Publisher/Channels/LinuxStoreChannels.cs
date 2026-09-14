using System;
using System.Collections.Generic;
using System.IO;

namespace Armor.Publisher.Channels
{
    /// <summary>
    /// Snap recipe generator. Emits a snapcraft.yaml that packages the published Linux payload.
    /// The workflow builds and pushes the .snap to the Snap Store (review-gated).
    /// </summary>
    public sealed class SnapChannel : IChannel
    {
        /// <inheritdoc/>
        public string Id => "snap";
        /// <inheritdoc/>
        public string DisplayName => "Snap package (Linux)";
        /// <inheritdoc/>
        public HostOs RequiredHost => HostOs.Any;

        /// <inheritdoc/>
        public IReadOnlyList<string> Build(ChannelContext context)
        {
            string snapName = context.Option("snapName", context.Config.Project.Name.ToLowerInvariant());
            string grade = context.Option("grade", "stable");
            string confinement = context.Option("confinement", "classic");

            string yaml =
                $"name: {snapName}\n" +
                $"version: '{context.Version}'\n" +
                $"summary: {context.Config.Project.DisplayName}\n" +
                $"description: |\n  {context.Config.Project.Description}\n" +
                "base: core22\n" +
                $"grade: {grade}\n" +
                $"confinement: {confinement}\n\n" +
                "apps:\n" +
                $"  {snapName}:\n" +
                $"    command: bin/{context.ExeName}\n" +
                "    daemon: simple\n" +
                "    restart-condition: on-failure\n\n" +
                "parts:\n" +
                $"  {snapName}:\n" +
                "    plugin: dump\n" +
                "    source: ./payload\n" +
                "    organize:\n" +
                $"      '*': bin/\n";

            string dir = Path.Combine(context.OutputDir, "manifests", "snap");
            Directory.CreateDirectory(dir);
            string outPath = Path.Combine(dir, "snapcraft.yaml");
            File.WriteAllText(outPath, yaml);
            Console.WriteLine($"[snap] wrote {outPath} (stage the linux payload under ./payload, then snapcraft)");
            return new[] { outPath };
        }
    }

    /// <summary>
    /// Flatpak recipe generator. Emits a manifest that installs the published Linux payload.
    /// The workflow builds with flatpak-builder and submits to Flathub (review-gated).
    /// </summary>
    public sealed class FlatpakChannel : IChannel
    {
        /// <inheritdoc/>
        public string Id => "flatpak";
        /// <inheritdoc/>
        public string DisplayName => "Flatpak package (Linux)";
        /// <inheritdoc/>
        public HostOs RequiredHost => HostOs.Any;

        /// <inheritdoc/>
        public IReadOnlyList<string> Build(ChannelContext context)
        {
            string appId = context.Option("appId", "com.joelchristner." + context.Config.Project.Name);

            string yaml =
                $"app-id: {appId}\n" +
                "runtime: org.freedesktop.Platform\n" +
                "runtime-version: '23.08'\n" +
                "sdk: org.freedesktop.Sdk\n" +
                $"command: {context.ExeName}\n" +
                "finish-args:\n" +
                "  - --share=network\n" +
                "  - --filesystem=home\n" +
                "modules:\n" +
                $"  - name: {context.Config.Project.Name.ToLowerInvariant()}\n" +
                "    buildsystem: simple\n" +
                "    build-commands:\n" +
                $"      - install -Dm755 {context.ExeName} /app/bin/{context.ExeName}\n" +
                "    sources:\n" +
                "      - type: dir\n" +
                "        path: ./payload\n";

            string dir = Path.Combine(context.OutputDir, "manifests", "flatpak");
            Directory.CreateDirectory(dir);
            string outPath = Path.Combine(dir, appId + ".yaml");
            File.WriteAllText(outPath, yaml);
            Console.WriteLine($"[flatpak] wrote {outPath} (stage the linux payload under ./payload, then flatpak-builder)");
            return new[] { outPath };
        }
    }
}
