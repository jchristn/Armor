using System.IO;

namespace Armor.Publisher
{
    /// <summary>Small filesystem helpers shared by channels.</summary>
    public static class FileSystemUtil
    {
        /// <summary>Recursively copies the contents of <paramref name="source"/> into <paramref name="dest"/>.</summary>
        public static void CopyTree(string source, string dest)
        {
            Directory.CreateDirectory(dest);
            foreach (string dir in Directory.GetDirectories(source, "*", SearchOption.AllDirectories))
                Directory.CreateDirectory(dir.Replace(source, dest));
            foreach (string file in Directory.GetFiles(source, "*", SearchOption.AllDirectories))
                File.Copy(file, file.Replace(source, dest), true);
        }
    }
}
