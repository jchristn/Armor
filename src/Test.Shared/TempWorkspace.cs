namespace Test.Shared
{
    using System;
    using System.IO;

    /// <summary>
    /// A disposable temporary directory for a single test case. Each instance creates a unique
    /// directory under the system temp path and removes it (recursively, best-effort) on disposal, so
    /// cases leave no residue on disk.
    /// </summary>
    public sealed class TempWorkspace : IDisposable
    {
        private readonly string _RootDirectory;
        private bool _Disposed;

        /// <summary>
        /// Absolute path to this workspace's root directory.
        /// </summary>
        public string RootDirectory
        {
            get { return _RootDirectory; }
        }

        /// <summary>
        /// Initializes a new instance of the <see cref="TempWorkspace"/> class and creates its
        /// directory.
        /// </summary>
        public TempWorkspace()
        {
            _RootDirectory = Path.Combine(Path.GetTempPath(), "armor-test-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(_RootDirectory);
        }

        /// <summary>
        /// Combine a relative path onto the workspace root.
        /// </summary>
        /// <param name="relative">Relative path segments joined onto the root.</param>
        /// <returns>The combined absolute path.</returns>
        public string Combine(string relative)
        {
            return Path.Combine(_RootDirectory, relative);
        }

        /// <summary>
        /// Remove the workspace directory. Errors during deletion are ignored.
        /// </summary>
        public void Dispose()
        {
            if (_Disposed)
                return;
            _Disposed = true;

            try
            {
                if (Directory.Exists(_RootDirectory))
                    Directory.Delete(_RootDirectory, true);
            }
            catch (IOException)
            {
            }
            catch (UnauthorizedAccessException)
            {
            }
        }
    }
}
