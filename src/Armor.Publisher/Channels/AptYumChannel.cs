using System;
using System.Collections.Generic;
using System.IO;

namespace Armor.Publisher.Channels
{
    /// <summary>
    /// Builds signed apt and yum repository metadata from the .deb/.rpm already in the output dir,
    /// laid out ready to publish to GitHub Pages. Users then add the repo and `apt-get install` /
    /// `dnf install`. Repository metadata is GPG-signed (not individual packages). Requires Linux
    /// with dpkg-dev, apt-utils, createrepo_c, and gpg.
    /// </summary>
    public sealed class AptYumChannel : IChannel
    {
        /// <inheritdoc/>
        public string Id => "aptyum";
        /// <inheritdoc/>
        public string DisplayName => "APT/YUM repositories (Linux)";
        /// <inheritdoc/>
        public HostOs RequiredHost => HostOs.Linux;

        /// <inheritdoc/>
        public IReadOnlyList<string> Build(ChannelContext context)
        {
            string[] debs = Directory.GetFiles(context.OutputDir, "*.deb", SearchOption.AllDirectories);
            string[] rpms = Directory.GetFiles(context.OutputDir, "*.rpm", SearchOption.AllDirectories);
            if (debs.Length == 0 && rpms.Length == 0)
                throw new FileNotFoundException(
                    $"No .deb/.rpm found in {context.OutputDir}. Run the 'debrpm' channel first " +
                    "(in CI, download the debrpm artifact into the output dir before this channel).");

            bool sign = context.Config.Signing.Linux.IsConfigured;
            if (!sign) Console.WriteLine("[aptyum] signing.linux.isConfigured=false — repo metadata will be UNSIGNED (apt/yum will warn).");

            string repo = Path.Combine(context.OutputDir, "repo");
            List<string> outputs = new List<string>();

            outputs.Add(BuildApt(context, repo, debs, sign));
            outputs.Add(BuildYum(context, repo, rpms, sign));

            if (sign)
            {
                string key = Path.Combine(repo, "armor-archive-keyring.asc");
                RunSh($"gpg --armor --export > \"{key}\"");
                outputs.Add(key);
            }
            return outputs;
        }

        private string BuildApt(ChannelContext context, string repo, string[] debs, bool sign)
        {
            string aptDir = Path.Combine(repo, "deb");
            string pool = Path.Combine(aptDir, "pool");
            Directory.CreateDirectory(pool);
            foreach (string d in debs) File.Copy(d, Path.Combine(pool, Path.GetFileName(d)), true);

            RunSh($"cd \"{aptDir}\" && dpkg-scanpackages --multiversion pool > Packages && gzip -kf Packages && apt-ftparchive release . > Release");
            if (sign)
            {
                RunSh($"cd \"{aptDir}\" && gpg --batch --yes --clearsign -o InRelease Release && gpg --batch --yes -abs -o Release.gpg Release");
            }
            Console.WriteLine($"[aptyum] apt repo at {aptDir}");
            return aptDir;
        }

        private string BuildYum(ChannelContext context, string repo, string[] rpms, bool sign)
        {
            string yumDir = Path.Combine(repo, "rpm");
            Directory.CreateDirectory(yumDir);
            foreach (string r in rpms) File.Copy(r, Path.Combine(yumDir, Path.GetFileName(r)), true);

            RunSh($"createrepo_c \"{yumDir}\"");
            if (sign)
            {
                RunSh($"gpg --batch --yes --detach-sign --armor \"{yumDir}/repodata/repomd.xml\"");
            }
            Console.WriteLine($"[aptyum] yum repo at {yumDir}");
            return yumDir;
        }

        /// <summary>Runs a shell one-liner via /bin/sh -c.</summary>
        private static void RunSh(string command) => ProcessRunner.Run("/bin/sh", new[] { "-c", command });
    }
}
