using System;
using System.Collections.Generic;
using System.IO;

namespace Armor.Publisher.Channels
{
    /// <summary>
    /// Linux .deb/.rpm channel. Publishes the agent per linux rid, lays out a common install tree
    /// (payload under /opt/armor, a /usr/bin symlink, and a systemd unit), and builds both a .deb
    /// and an .rpm from that same tree with fpm. Requires Linux with fpm installed.
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
                string payloadDir = context.PublishPayload(rid);

                string root = Path.Combine(context.StagingDir, $"pkgroot-{rid}");
                if (Directory.Exists(root)) Directory.Delete(root, true);
                Directory.CreateDirectory(Path.Combine(root, "opt", "armor"));
                Directory.CreateDirectory(Path.Combine(root, "usr", "bin"));
                Directory.CreateDirectory(Path.Combine(root, "usr", "lib", "systemd", "system"));

                FileSystemUtil.CopyTree(payloadDir, Path.Combine(root, "opt", "armor"));
                File.WriteAllText(Path.Combine(root, "usr", "lib", "systemd", "system", "armor-agent.service"), SystemdUnit(context));
                // Relative symlink so it resolves against the package's install root.
                ProcessRunner.Run("ln", new[] { "-sf", $"/opt/armor/{context.ExeName}", Path.Combine(root, "usr", "bin", "armor-agent") });

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
            ProcessRunner.Run("fpm", new[]
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
                "-C", root,
                "opt", "usr"
            });

            context.EmitChecksum(outPath);
            return outPath;
        }

        private static string MaintainerOrDefault(ChannelContext c) =>
            string.IsNullOrWhiteSpace(c.Config.Project.Maintainer) ? "Joel Christner <joel.christner@gmail.com>" : c.Config.Project.Maintainer;

        private static string DescriptionOrDefault(ChannelContext c) =>
            string.IsNullOrWhiteSpace(c.Config.Project.Description) ? "Armor backup agent" : c.Config.Project.Description;

        private static string SystemdUnit(ChannelContext context) =>
            "[Unit]\nDescription=Armor backup agent\nAfter=network.target\n\n" +
            $"[Service]\nExecStart=/opt/armor/{context.ExeName}\nRestart=on-failure\n\n" +
            "[Install]\nWantedBy=multi-user.target\n";

        private static string AfterInstall() =>
            "#!/bin/sh\nset -e\n" +
            "chmod +x /opt/armor/Armor.Agent 2>/dev/null || true\n" +
            "systemctl daemon-reload 2>/dev/null || true\n" +
            "systemctl enable --now armor-agent 2>/dev/null || true\n";

        private static string BeforeRemove() =>
            "#!/bin/sh\nset -e\n" +
            "systemctl disable --now armor-agent 2>/dev/null || true\n";
    }
}
