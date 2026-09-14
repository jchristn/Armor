using System;
using System.Collections.Generic;
using System.IO;

namespace Armor.Publisher.Channels
{
    /// <summary>
    /// macOS channel. For each osx rid it builds a proper .app bundle, codesigns it (hardened
    /// runtime), then produces both a .dmg (drag-to-Applications download) and a .pkg that installs
    /// the app and registers a LaunchAgent so the daemon starts at login. Both are notarized and
    /// stapled when signing is configured. Requires macOS.
    /// </summary>
    public sealed class DmgChannel : IChannel
    {
        /// <inheritdoc/>
        public string Id => "dmg";

        /// <inheritdoc/>
        public string DisplayName => "Apple Disk Image + Installer Package (macOS)";

        /// <inheritdoc/>
        public HostOs RequiredHost => HostOs.MacOs;

        /// <inheritdoc/>
        public IReadOnlyList<string> Build(ChannelContext context)
        {
            List<string> outputs = new List<string>();
            string bundleId = "com.joelchristner." + context.Config.Project.Name.ToLowerInvariant();

            foreach (string rid in context.Runtimes)
            {
                Console.WriteLine($"[dmg] publishing {context.Artifact.Id} for {rid}");
                string payloadDir = context.PublishPayload(rid);

                string appName = context.Config.Project.DisplayName + ".app";
                string appStage = Path.Combine(context.StagingDir, $"bundle-{rid}");
                string appRoot = Path.Combine(appStage, appName);
                string macOsDir = Path.Combine(appRoot, "Contents", "MacOS");
                string resourcesDir = Path.Combine(appRoot, "Contents", "Resources");
                if (Directory.Exists(appStage)) Directory.Delete(appStage, true);
                Directory.CreateDirectory(macOsDir);
                Directory.CreateDirectory(resourcesDir);

                FileSystemUtil.CopyTree(payloadDir, macOsDir);

                WriteInfoPlist(Path.Combine(appRoot, "Contents", "Info.plist"), context, bundleId);

                // Bundle the LaunchAgent plist so the .pkg postinstall can install it.
                File.WriteAllText(Path.Combine(resourcesDir, bundleId + ".plist"), LaunchAgentPlist(context, bundleId));

                string exePath = Path.Combine(macOsDir, context.ExeName);
                if (File.Exists(exePath)) ProcessRunner.Run("chmod", new[] { "+x", exePath });

                Signing.MacCodesign(appRoot, context.Config.Signing.MacOs);

                outputs.Add(BuildDmg(context, rid, appStage));
                outputs.Add(BuildPkg(context, rid, appRoot, appName, bundleId));
            }

            return outputs;
        }

        private static string BuildDmg(ChannelContext context, string rid, string appStage)
        {
            string dmgName = $"{context.Config.Project.Name}-{context.Version}-{rid}.dmg";
            string dmgPath = Path.Combine(context.OutputDir, dmgName);
            if (File.Exists(dmgPath)) File.Delete(dmgPath);

            Console.WriteLine($"[dmg] creating {dmgName}");
            ProcessRunner.Run("hdiutil", new[]
            {
                "create", "-volname", context.Config.Project.DisplayName,
                "-srcfolder", appStage, "-ov", "-format", "UDZO", dmgPath
            });

            Signing.MacNotarizeStaple(dmgPath, context.Config.Signing.MacOs);
            context.EmitChecksum(dmgPath);
            return dmgPath;
        }

        private static string BuildPkg(ChannelContext context, string rid, string appRoot, string appName, string bundleId)
        {
            // pkgbuild needs a component root whose layout mirrors the install location.
            string pkgRoot = Path.Combine(context.StagingDir, $"pkgroot-{rid}", "Applications");
            if (Directory.Exists(Path.GetDirectoryName(pkgRoot)!)) Directory.Delete(Path.GetDirectoryName(pkgRoot)!, true);
            Directory.CreateDirectory(pkgRoot);
            FileSystemUtil.CopyTree(appRoot, Path.Combine(pkgRoot, appName));

            string scriptsDir = Path.Combine(context.StagingDir, $"pkgscripts-{rid}");
            Directory.CreateDirectory(scriptsDir);
            string postinstall = Path.Combine(scriptsDir, "postinstall");
            File.WriteAllText(postinstall, PostInstallScript(context, bundleId, appName));
            ProcessRunner.Run("chmod", new[] { "+x", postinstall });

            string pkgName = $"{context.Config.Project.Name}-{context.Version}-{rid}.pkg";
            string pkgPath = Path.Combine(context.OutputDir, pkgName);
            if (File.Exists(pkgPath)) File.Delete(pkgPath);

            Console.WriteLine($"[dmg] creating {pkgName}");
            ProcessRunner.Run("pkgbuild", new[]
            {
                "--root", Path.GetDirectoryName(pkgRoot)!,
                "--identifier", bundleId,
                "--version", context.Version,
                "--scripts", scriptsDir,
                "--install-location", "/",
                pkgPath
            });

            Signing.MacNotarizeStaple(pkgPath, context.Config.Signing.MacOs);
            context.EmitChecksum(pkgPath);
            return pkgPath;
        }

        private static void WriteInfoPlist(string path, ChannelContext context, string bundleId)
        {
            string plist =
                "<?xml version=\"1.0\" encoding=\"UTF-8\"?>\n" +
                "<!DOCTYPE plist PUBLIC \"-//Apple//DTD PLIST 1.0//EN\" \"http://www.apple.com/DTDs/PropertyList-1.0.dtd\">\n" +
                "<plist version=\"1.0\">\n<dict>\n" +
                $"  <key>CFBundleName</key><string>{context.Config.Project.DisplayName}</string>\n" +
                $"  <key>CFBundleDisplayName</key><string>{context.Config.Project.DisplayName}</string>\n" +
                $"  <key>CFBundleIdentifier</key><string>{bundleId}</string>\n" +
                $"  <key>CFBundleVersion</key><string>{context.Version}</string>\n" +
                $"  <key>CFBundleShortVersionString</key><string>{context.Version}</string>\n" +
                $"  <key>CFBundleExecutable</key><string>{context.ExeName}</string>\n" +
                "  <key>CFBundlePackageType</key><string>APPL</string>\n" +
                "  <key>LSMinimumSystemVersion</key><string>11.0</string>\n" +
                "  <key>LSUIElement</key><true/>\n" +
                "</dict>\n</plist>\n";
            File.WriteAllText(path, plist);
        }

        private static string LaunchAgentPlist(ChannelContext context, string bundleId)
        {
            string exe = $"/Applications/{context.Config.Project.DisplayName}.app/Contents/MacOS/{context.ExeName}";
            return
                "<?xml version=\"1.0\" encoding=\"UTF-8\"?>\n" +
                "<!DOCTYPE plist PUBLIC \"-//Apple//DTD PLIST 1.0//EN\" \"http://www.apple.com/DTDs/PropertyList-1.0.dtd\">\n" +
                "<plist version=\"1.0\">\n<dict>\n" +
                $"  <key>Label</key><string>{bundleId}</string>\n" +
                $"  <key>ProgramArguments</key><array><string>{exe}</string></array>\n" +
                "  <key>RunAtLoad</key><true/>\n" +
                "  <key>KeepAlive</key><true/>\n" +
                "</dict>\n</plist>\n";
        }

        private static string PostInstallScript(ChannelContext context, string bundleId, string appName)
        {
            string plistSrc = $"/Applications/{appName}/Contents/Resources/{bundleId}.plist";
            return
                "#!/bin/sh\n" +
                "set -e\n" +
                $"cp \"{plistSrc}\" /Library/LaunchAgents/{bundleId}.plist\n" +
                "chmod 644 /Library/LaunchAgents/" + bundleId + ".plist\n" +
                "# Load for the console user so it starts now as well as at future logins.\n" +
                "uid=$(/usr/bin/id -u \"${USER:-$(/usr/bin/stat -f%Su /dev/console)}\")\n" +
                $"/bin/launchctl bootstrap gui/$uid /Library/LaunchAgents/{bundleId}.plist 2>/dev/null || true\n" +
                "exit 0\n";
        }
    }
}
