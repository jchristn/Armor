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
                string payloadDir = context.PublishCombinedPayload(rid);

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

            AttachLicenseAgreement(context, dmgPath);

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

            // Build an intermediate component package. On its own a pkgbuild output cannot show a
            // license pane, so we wrap it below with productbuild + a distribution file that does.
            string componentDir = Path.Combine(context.StagingDir, $"component-{rid}");
            if (Directory.Exists(componentDir)) Directory.Delete(componentDir, true);
            Directory.CreateDirectory(componentDir);
            string componentPkg = Path.Combine(componentDir, "component.pkg");

            Console.WriteLine($"[dmg] building component package for {rid}");
            ProcessRunner.Run("pkgbuild", new[]
            {
                "--root", Path.GetDirectoryName(pkgRoot)!,
                "--identifier", bundleId,
                "--version", context.Version,
                "--scripts", scriptsDir,
                "--install-location", "/",
                componentPkg
            });

            // The distribution file references a license file staged in a resources dir. Installer.app
            // then shows a License pane with a mandatory Agree/Disagree sheet before it will proceed.
            string resourcesDir = Path.Combine(context.StagingDir, $"pkgres-{rid}");
            if (Directory.Exists(resourcesDir)) Directory.Delete(resourcesDir, true);
            Directory.CreateDirectory(resourcesDir);
            bool hasLicense = StagePkgLicense(context, resourcesDir);

            string distPath = Path.Combine(context.StagingDir, $"distribution-{rid}.xml");
            File.WriteAllText(distPath, DistributionXml(context, bundleId, "component.pkg", hasLicense));

            string pkgName = $"{context.Config.Project.Name}-{context.Version}-{rid}.pkg";
            string pkgPath = Path.Combine(context.OutputDir, pkgName);
            if (File.Exists(pkgPath)) File.Delete(pkgPath);

            Console.WriteLine($"[dmg] creating {pkgName}");
            ProcessRunner.Run("productbuild", new[]
            {
                "--distribution", distPath,
                "--package-path", componentDir,
                "--resources", resourcesDir,
                pkgPath
            });

            Signing.MacNotarizeStaple(pkgPath, context.Config.Signing.MacOs);
            context.EmitChecksum(pkgPath);
            return pkgPath;
        }

        /// <summary>
        /// Copies the project's license into a productbuild resources dir as LICENSE.txt. Returns
        /// false (and warns) when no license file exists, so the distribution omits the license pane.
        /// </summary>
        private static bool StagePkgLicense(ChannelContext context, string resourcesDir)
        {
            string? src = context.LicenseFile();
            if (src == null)
            {
                Console.WriteLine("[dmg] WARNING: no license file found at repo root; the .pkg will not show a license pane.");
                return false;
            }
            File.Copy(src, Path.Combine(resourcesDir, "LICENSE.txt"), overwrite: true);
            return true;
        }

        /// <summary>
        /// Renders a productbuild distribution file for a single-component install. When
        /// <paramref name="hasLicense"/> is true it adds a &lt;license&gt; element, which makes
        /// Installer.app present a mandatory license-acceptance pane.
        /// </summary>
        private static string DistributionXml(ChannelContext context, string bundleId, string pkgRef, bool hasLicense)
        {
            string title = context.Config.Project.DisplayName;
            string licenseLine = hasLicense ? "  <license file=\"LICENSE.txt\" mime-type=\"text/plain\"/>\n" : "";
            return
                "<?xml version=\"1.0\" encoding=\"utf-8\"?>\n" +
                "<installer-gui-script minSpecVersion=\"2\">\n" +
                $"  <title>{title}</title>\n" +
                licenseLine +
                $"  <choices-outline>\n    <line choice=\"default\"/>\n  </choices-outline>\n" +
                $"  <choice id=\"default\" title=\"{title}\">\n    <pkg-ref id=\"{bundleId}\"/>\n  </choice>\n" +
                $"  <pkg-ref id=\"{bundleId}\" version=\"{context.Version}\" onConclusion=\"none\">{pkgRef}</pkg-ref>\n" +
                "</installer-gui-script>\n";
        }

        /// <summary>
        /// Attaches a Software License Agreement to a .dmg so the volume shows an Agree/Disagree
        /// prompt before it will mount. Best-effort: attaching resources is a macOS-only step that
        /// can vary by OS version, so a failure warns rather than aborting the (already built) DMG.
        /// </summary>
        private static void AttachLicenseAgreement(ChannelContext context, string dmgPath)
        {
            string? licenseSrc = context.LicenseFile();
            if (licenseSrc == null)
            {
                Console.WriteLine("[dmg] WARNING: no license file found at repo root; the .dmg will not show a license agreement.");
                return;
            }

            // Classic SLA resources expect CR line endings for correct display.
            string licenseText = File.ReadAllText(licenseSrc).Replace("\r\n", "\n").Replace("\n", "\r");
            string plistPath = Path.Combine(context.StagingDir, "sla.plist");
            File.WriteAllText(plistPath, SlaPlist(licenseText));

            Console.WriteLine("[dmg] attaching software license agreement");
            try
            {
                ProcessRunner.Run("hdiutil", new[] { "udifrez", "-xml", plistPath, "", dmgPath });
            }
            catch (ProcessFailedException ex)
            {
                Console.Error.WriteLine($"[dmg] WARNING: could not attach the license agreement ({ex.Message}). " +
                    "The DMG was still produced; verify SLA support on this macOS version. The .pkg license pane is unaffected.");
            }
        }

        /// <summary>
        /// Builds the SLA resource plist consumed by "hdiutil udifrez -xml". LPic selects the default
        /// language; STR# holds the button labels; TEXT holds the agreement body. The LPic/STR# byte
        /// blobs are the standard English resources used by Apple's SLA tooling.
        /// </summary>
        private static string SlaPlist(string licenseText)
        {
            // LPic: defaultLanguageID=0, one localization mapping system language 0 to resource
            // index 0 (Apple's canonical English default). See Apple's SLAResources sample.
            const string lpic = "AAAAAQAAAAAAAA==";
            // STR# built from the actual labels so the byte layout (2-byte count + Pascal strings)
            // is always correct rather than a hand-copied blob.
            string strButtons = BuildStrResource(new[]
            {
                "English", "Agree", "Disagree", "Print", "Save...",
                "If you agree with the terms of this license, press \"Agree\" to install the software. " +
                "If you do not agree, press \"Disagree\"."
            });
            string textB64 = System.Convert.ToBase64String(System.Text.Encoding.UTF8.GetBytes(licenseText));

            return
                "<?xml version=\"1.0\" encoding=\"UTF-8\"?>\n" +
                "<!DOCTYPE plist PUBLIC \"-//Apple//DTD PLIST 1.0//EN\" \"http://www.apple.com/DTDs/PropertyList-1.0.dtd\">\n" +
                "<plist version=\"1.0\">\n<dict>\n" +
                "  <key>LPic</key>\n  <array>\n    <dict>\n" +
                "      <key>Attributes</key><string>0x0000</string>\n" +
                "      <key>ID</key><string>5000</string>\n" +
                "      <key>Name</key><string></string>\n" +
                $"      <key>Data</key>\n      <data>{lpic}</data>\n" +
                "    </dict>\n  </array>\n" +
                "  <key>STR#</key>\n  <array>\n    <dict>\n" +
                "      <key>Attributes</key><string>0x0000</string>\n" +
                "      <key>ID</key><string>5000</string>\n" +
                "      <key>Name</key><string>English buttons</string>\n" +
                $"      <key>Data</key>\n      <data>{strButtons}</data>\n" +
                "    </dict>\n  </array>\n" +
                "  <key>TEXT</key>\n  <array>\n    <dict>\n" +
                "      <key>Attributes</key><string>0x0000</string>\n" +
                "      <key>ID</key><string>5000</string>\n" +
                "      <key>Name</key><string>English SLA</string>\n" +
                $"      <key>Data</key>\n      <data>{textB64}</data>\n" +
                "    </dict>\n  </array>\n" +
                "</dict>\n</plist>\n";
        }

        /// <summary>
        /// Encodes a classic Mac STR# resource as base64: a big-endian 2-byte string count followed
        /// by length-prefixed Pascal strings (one length byte + up to 255 bytes each).
        /// </summary>
        private static string BuildStrResource(string[] items)
        {
            using MemoryStream ms = new MemoryStream();
            ms.WriteByte((byte)((items.Length >> 8) & 0xFF));
            ms.WriteByte((byte)(items.Length & 0xFF));
            foreach (string s in items)
            {
                byte[] bytes = System.Text.Encoding.ASCII.GetBytes(s);
                int len = bytes.Length > 255 ? 255 : bytes.Length;
                ms.WriteByte((byte)len);
                ms.Write(bytes, 0, len);
            }
            return System.Convert.ToBase64String(ms.ToArray());
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
                CliSymlinks(context, appName) +
                "exit 0\n";
        }

        /// <summary>
        /// Emits shell lines that link each bundled CLI (the "include"d artifacts, which live inside
        /// the .app) into /usr/local/bin so it is runnable from the shell. The first include takes
        /// the project name as its command (matching the dotnet-tool command); any others use their
        /// own executable name. Empty when nothing extra is bundled.
        /// </summary>
        private static string CliSymlinks(ChannelContext context, string appName)
        {
            IReadOnlyList<string> includes = context.IncludedArtifacts;
            if (includes.Count == 0) return "";

            string sh = "mkdir -p /usr/local/bin\n";
            for (int i = 0; i < includes.Count; i++)
            {
                string exe = context.ExeNameOf(includes[i]);
                string cmd = i == 0 ? context.Config.Project.Name.ToLowerInvariant() : exe.ToLowerInvariant();
                string target = $"/Applications/{appName}/Contents/MacOS/{exe}";
                sh += $"ln -sf \"{target}\" /usr/local/bin/{cmd}\n";
            }
            return sh;
        }
    }
}
