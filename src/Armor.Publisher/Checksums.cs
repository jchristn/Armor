using System;
using System.Collections.Generic;
using System.IO;
using System.Security.Cryptography;
using System.Text;

namespace Armor.Publisher
{
    /// <summary>
    /// SHA-256 helpers. Every produced artifact carries a checksum emitted at build time; the
    /// package-manager channels reference these rather than a hand-copied hash.
    /// </summary>
    public static class Checksums
    {
        /// <summary>Computes the lowercase hex SHA-256 of a file.</summary>
        public static string Sha256(string file)
        {
            using FileStream fs = File.OpenRead(file);
            using SHA256 sha = SHA256.Create();
            byte[] hash = sha.ComputeHash(fs);
            StringBuilder sb = new StringBuilder(hash.Length * 2);
            foreach (byte b in hash) sb.Append(b.ToString("x2"));
            return sb.ToString();
        }

        /// <summary>Writes "&lt;file&gt;.sha256" containing "&lt;hash&gt;  &lt;filename&gt;" and returns the hash.</summary>
        public static string WriteSidecar(string file)
        {
            string hash = Sha256(file);
            File.WriteAllText(file + ".sha256", $"{hash}  {Path.GetFileName(file)}\n");
            return hash;
        }

        /// <summary>Writes an aggregate SHA256SUMS file covering the given files, returns its path.</summary>
        public static string WriteManifest(string outputDir, IEnumerable<string> files)
        {
            string path = Path.Combine(outputDir, "SHA256SUMS");
            StringBuilder sb = new StringBuilder();
            foreach (string f in files)
            {
                if (!File.Exists(f)) continue;
                sb.Append(Sha256(f)).Append("  ").Append(Path.GetFileName(f)).Append('\n');
            }
            File.WriteAllText(path, sb.ToString());
            return path;
        }
    }
}
