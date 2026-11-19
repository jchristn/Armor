using System;
using System.Collections.Generic;
using System.IO;

namespace Armor.Publisher.Channels
{
    /// <summary>
    /// Linux .deb/.rpm channel. Publishes the agent per linux rid, lays out a common install tree
    /// (payload under /opt/armor, /usr/bin symlinks, an application-menu entry with icon, and a
    /// desktop autostart entry), and builds both a .deb and an .rpm from that same tree with fpm.
    /// The agent is a tray app that belongs to the logged-in user (it needs their display and their
    /// ~/.armor), so it starts with each user's desktop session through /etc/xdg/autostart rather
    /// than as a root system service. Requires Linux with fpm installed.
    /// </summary>
    public sealed class DebRpmChannel : IChannel
    {
        /// <inheritdoc/>
        public string Id => "debrpm";

        /// <inheritdoc/>
        public string DisplayName => "Debian/RPM packages (Linux)";

        /// <inheritdoc/>
        public HostOs RequiredHost => HostOs.Linux;

        /// <inheritdoc/>
        public IReadOnlyList<string> Build(ChannelContext context)
        {
            List<string> outputs = new List<string>();
            string pkg = context.Config.Project.Name.ToLowerInvariant();

            foreach (string rid in context.Runtimes)
            {
                Console.WriteLine($"[debrpm] publishing {context.Artifact.Id} for {rid}");
                string payloadDir = context.PublishCombinedPayload(rid);

                string root = Path.Combine(context.StagingDir, $"pkgroot-{rid}");
                if (Directory.Exists(root)) Directory.Delete(root, true);
                Directory.CreateDirectory(Path.Combine(root, "opt", "armor"));
                Directory.CreateDirectory(Path.Combine(root, "usr", "bin"));
                Directory.CreateDirectory(Path.Combine(root, "etc", "xdg", "autostart"));
                Directory.CreateDirectory(Path.Combine(root, "usr", "share", "applications"));

                FileSystemUtil.CopyTree(payloadDir, Path.Combine(root, "opt", "armor"));
                BundleLicense(context, root, pkg);
                File.WriteAllText(Path.Combine(root, "etc", "xdg", "autostart", "armor-agent.desktop"), AutostartEntry());
                File.WriteAllText(Path.Combine(root, "usr", "share", "applications", "armor-agent.desktop"), MenuEntry(context));
                BundleIcon(context, root);
                // Relative symlink so it resolves against the package's install root.
                ProcessRunner.Run("ln", new[] { "-sf", $"/opt/armor/{context.ExeName}", Path.Combine(root, "usr", "bin", "armor-agent") });

                // Expose each bundled CLI on PATH. The first include takes the project name as its
                // command (matching the dotnet-tool command); any others use their executable name.
                IReadOnlyList<string> includes = context.IncludedArtifacts;
                for (int i = 0; i < includes.Count; i++)
                {
                    string exe = context.ExeNameOf(includes[i]);
                    string cmd = i == 0 ? pkg : exe.ToLowerInvariant();
                    ProcessRunner.Run("ln", new[] { "-sf", $"/opt/armor/{exe}", Path.Combine(root, "usr", "bin", cmd) });
                }

                string scripts = Path.Combine(context.StagingDir, $"scripts-{rid}");
                Directory.CreateDirectory(scripts);
                string afterInstall = Path.Combine(scripts, "after-install.sh");
                string beforeRemove = Path.Combine(scripts, "before-remove.sh");
                File.WriteAllText(afterInstall, AfterInstall());
                File.WriteAllText(beforeRemove, BeforeRemove());

                string debArch = rid.EndsWith("arm64", StringComparison.OrdinalIgnoreCase) ? "arm64" : "amd64";
                string rpmArch = rid.EndsWith("arm64", StringComparison.OrdinalIgnoreCase) ? "aarch64" : "x86_64";

                outputs.Add(RunFpm(context, "deb", debArch, root, afterInstall, beforeRemove,
                    Path.Combine(context.OutputDir, $"{pkg}_{context.Version}_{debArch}.deb")));
                outputs.Add(RunFpm(context, "rpm", rpmArch, root, afterInstall, beforeRemove,
                    Path.Combine(context.OutputDir, $"{pkg}-{context.Version}-1.{rpmArch}.rpm")));
            }

            return outputs;
        }

        private static string RunFpm(ChannelContext context, string target, string arch, string root,
            string afterInstall, string beforeRemove, string outPath)
        {
            string pkg = context.Config.Project.Name.ToLowerInvariant();
            if (File.Exists(outPath)) File.Delete(outPath);

            Console.WriteLine($"[debrpm] fpm -t {target} ({arch})");
            List<string> args = new List<string>
            {
                "-s", "dir", "-t", target,
                "-n", pkg,
                "-v", context.Version,
                "-a", arch,
                "--license", context.Config.Project.License,
                "--maintainer", MaintainerOrDefault(context),
                "--url", context.Config.Project.Repo,
                "--description", DescriptionOrDefault(context),
                "--after-install", afterInstall,
                "--before-remove", beforeRemove,
                "--package", outPath,
                "-C", root
            };
            foreach (string dep in Dependencies(target))
            {
                args.Add("-d");
                args.Add(dep);
            }
            args.AddRange(new[] { "opt", "usr", "etc" });
            ProcessRunner.Run("fpm", args);

            context.EmitChecksum(outPath);
            return outPath;
        }

        /// <summary>
        /// Copies the license into the install tree next to the payload (/opt/armor) and at the
        /// Debian doc convention (/usr/share/doc/&lt;pkg&gt;/copyright). .deb/.rpm installs are
        /// non-interactive, so the license ships on disk rather than as a click-through prompt.
        /// </summary>
        private static void BundleLicense(ChannelContext context, string root, string pkg)
        {
            string? src = context.LicenseFile();
            if (src == null)
            {
                Console.WriteLine("[debrpm] WARNING: no license file found at repo root; packages will carry license metadata only.");
                return;
            }

            File.Copy(src, Path.Combine(root, "opt", "armor", Path.GetFileName(src)), overwrite: true);

            string docDir = Path.Combine(root, "usr", "share", "doc", pkg);
            Directory.CreateDirectory(docDir);
            File.Copy(src, Path.Combine(docDir, "copyright"), overwrite: true);
        }

        private static string MaintainerOrDefault(ChannelContext c) =>
            string.IsNullOrWhiteSpace(c.Config.Project.Maintainer) ? "Joel Christner <joel.christner@gmail.com>" : c.Config.Project.Maintainer;

        private static string DescriptionOrDefault(ChannelContext c) =>
            string.IsNullOrWhiteSpace(c.Config.Project.Description) ? "Armor backup agent" : c.Config.Project.Description;

        /// <summary>
        /// Shared libraries the self-contained agent loads at startup: ICU for .NET globalization,
        /// and the X11 client libraries and fontconfig that Avalonia (the tray UI) needs. These are
        /// the same libraries scripts/common/native-libs.sh checks for. ICU's Debian package name
        /// carries its version, so the .deb accepts any of the versions current distros ship.
        /// </summary>
        private static IEnumerable<string> Dependencies(string target)
        {
            if (target == "deb")
            {
                yield return "libicu78 | libicu77 | libicu76 | libicu74 | libicu72 | libicu71 | libicu70 | libicu67 | libicu66";
                foreach (string d in new[] { "libx11-6", "libice6", "libsm6", "libxext6", "libxrandr2", "libxi6", "libxcursor1", "libxfixes3", "libfontconfig1" })
                    yield return d;
            }
            else
            {
                foreach (string d in new[] { "libicu", "libX11", "libICE", "libSM", "libXext", "libXrandr", "libXi", "libXcursor", "libXfixes", "fontconfig" })
                    yield return d;
            }
        }

        /// <summary>Installs assets/logo.png as the hicolor "armor" icon the menu entry names.</summary>
        private static void BundleIcon(ChannelContext context, string root)
        {
            string logo = Path.Combine(context.RepoRoot, "assets", "logo.png");
            if (!File.Exists(logo))
            {
                Console.WriteLine("[debrpm] WARNING: assets/logo.png not found; the menu entry will use a generic icon.");
                return;
            }
            string iconDir = Path.Combine(root, "usr", "share", "icons", "hicolor", "512x512", "apps");
            Directory.CreateDirectory(iconDir);
            File.Copy(logo, Path.Combine(iconDir, "armor.png"), overwrite: true);
        }

        // Starts the agent with each user's desktop session (GNOME, KDE, Xfce, and any other
        // XDG-compliant desktop). A user can turn it off in their desktop's startup settings.
        private static string AutostartEntry() =>
            "[Desktop Entry]\n" +
            "Type=Application\n" +
            "Name=Armor Agent\n" +
            "Comment=Armor backup agent (tray scheduler)\n" +
            "Exec=/usr/bin/armor-agent\n" +
            "Icon=armor\n" +
            "Terminal=false\n" +
            "X-GNOME-Autostart-enabled=true\n";

        // Puts the agent in the application menu so it can be started without logging out.
        private static string MenuEntry(ChannelContext context) =>
            "[Desktop Entry]\n" +
            "Type=Application\n" +
            $"Name={context.Config.Project.DisplayName}\n" +
            "Comment=Backup agent with a tray icon; run 'armor' in a terminal for the console\n" +
            "Exec=/usr/bin/armor-agent\n" +
            "Icon=armor\n" +
            "Terminal=false\n" +
            "Categories=Utility;Archiving;\n";

        private static string AfterInstall() =>
            "#!/bin/sh\nset -e\n" +
            "chmod +x /opt/armor/Armor.Agent /opt/armor/Armor.Tui 2>/dev/null || true\n" +
            "gtk-update-icon-cache -q /usr/share/icons/hicolor 2>/dev/null || true\n" +
            "update-desktop-database -q /usr/share/applications 2>/dev/null || true\n";

        private static string BeforeRemove() =>
            "#!/bin/sh\n" +
            "# Stop running agents so the files can be removed; ~/.armor is left alone.\n" +
            "pkill -x Armor.Agent 2>/dev/null || true\n" +
            "exit 0\n";
    }
}
