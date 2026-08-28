namespace Armor.Tui.Widgets
{
    using System;
    using System.Collections.Generic;
    using System.IO;
    using TUIKit.Widgets;

    /// <summary>
    /// An <see cref="IFileSystemProvider"/> over the file list of a backup point-in-time, so the restore
    /// picker can navigate the captured tree hierarchically — drilling into folders and back out — instead
    /// of showing one flat list of every path. The tree is built once from the manifest's paths; only
    /// directories and files that were actually backed up appear, never the live disk.
    /// </summary>
    public sealed class ManifestFileSystemProvider : IFileSystemProvider
    {
        private sealed class Node
        {
            public string FullPath = String.Empty;
            public bool IsFile;
            public readonly Dictionary<string, Node> Children;

            public Node(IEqualityComparer<string> comparer)
            {
                Children = new Dictionary<string, Node>(comparer);
            }
        }

        private readonly StringComparer _Comparer;
        private readonly Dictionary<string, Node> _Nodes;
        private readonly SortedSet<string> _Roots;

        /// <summary>
        /// Initializes a new instance of the <see cref="ManifestFileSystemProvider"/> class.
        /// </summary>
        /// <param name="filePaths">Every file path captured in the point-in-time. Cannot be null.</param>
        /// <exception cref="ArgumentNullException">Thrown when <paramref name="filePaths"/> is null.</exception>
        public ManifestFileSystemProvider(IEnumerable<string> filePaths)
        {
            if (filePaths == null)
                throw new ArgumentNullException(nameof(filePaths));

            _Comparer = OperatingSystem.IsWindows() ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal;
            _Nodes = new Dictionary<string, Node>(_Comparer);
            _Roots = new SortedSet<string>(_Comparer);

            foreach (string raw in filePaths)
            {
                if (String.IsNullOrWhiteSpace(raw))
                    continue;
                Insert(NormalizeSeparators(raw));
            }
        }

        /// <inheritdoc/>
        public IEqualityComparer<string> PathComparer
        {
            get { return _Comparer; }
        }

        /// <inheritdoc/>
        public IReadOnlyList<string> GetRoots()
        {
            return new List<string>(_Roots);
        }

        /// <inheritdoc/>
        public IReadOnlyList<string> GetChildren(string path, bool includeFiles, bool includeHidden)
        {
            List<string> result = new List<string>();
            if (path == null || !_Nodes.TryGetValue(path, out Node? node) || node == null)
                return result;

            List<string> directories = new List<string>();
            List<string> files = new List<string>();
            foreach (Node child in node.Children.Values)
            {
                if (child.IsFile)
                    files.Add(child.FullPath);
                else
                    directories.Add(child.FullPath);
            }

            // Directories first, then files (only when requested), each ordered for a stable, readable list.
            directories.Sort(_Comparer);
            result.AddRange(directories);
            if (includeFiles)
            {
                files.Sort(_Comparer);
                result.AddRange(files);
            }
            return result;
        }

        /// <inheritdoc/>
        public bool IsDirectory(string path)
        {
            return path != null && _Nodes.TryGetValue(path, out Node? node) && node != null && !node.IsFile;
        }

        /// <inheritdoc/>
        public string DisplayName(string path)
        {
            if (String.IsNullOrEmpty(path))
                return String.Empty;
            if (_Roots.Contains(path))
                return path;
            string name = Path.GetFileName(path);
            return String.IsNullOrEmpty(name) ? path : name;
        }

        /// <inheritdoc/>
        public string Normalize(string path)
        {
            return path == null ? String.Empty : NormalizeSeparators(path);
        }

        private void Insert(string filePath)
        {
            Node fileNode = GetOrAdd(filePath);
            fileNode.IsFile = true;

            string child = filePath;
            while (true)
            {
                string? parent = Path.GetDirectoryName(child);
                if (String.IsNullOrEmpty(parent))
                {
                    _Roots.Add(child);
                    break;
                }

                Node parentNode = GetOrAdd(parent);
                parentNode.IsFile = false;
                parentNode.Children[child] = GetOrAdd(child);
                child = parent;
            }
        }

        private Node GetOrAdd(string path)
        {
            if (!_Nodes.TryGetValue(path, out Node? node) || node == null)
            {
                node = new Node(_Comparer) { FullPath = path };
                _Nodes[path] = node;
            }
            return node;
        }

        private static string NormalizeSeparators(string path)
        {
            char sep = Path.DirectorySeparatorChar;
            char other = sep == '\\' ? '/' : '\\';
            return path.Replace(other, sep);
        }
    }
}
