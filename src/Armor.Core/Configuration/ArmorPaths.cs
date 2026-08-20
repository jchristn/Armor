namespace Armor.Core.Configuration
{
    using System;
    using System.IO;

    /// <summary>
    /// Resolves the on-disk locations Armor uses: the configuration root directory and the
    /// configuration file, log directory, state directory, and default database file beneath it.
    /// The root defaults to <c>~/.armor</c> but can be overridden explicitly (used by tests) or via
    /// the <c>ARMOR_HOME</c> environment variable.
    /// </summary>
    public class ArmorPaths
    {
        private string _RootDirectory = String.Empty;

        /// <summary>
        /// Absolute path to the Armor configuration root directory. Cannot be null or whitespace.
        /// </summary>
        /// <exception cref="ArgumentNullException">Thrown when set to null or whitespace.</exception>
        public string RootDirectory
        {
            get
            {
                return _RootDirectory;
            }
            set
            {
                if (String.IsNullOrWhiteSpace(value))
                    throw new ArgumentNullException(nameof(RootDirectory));
                _RootDirectory = value;
            }
        }

        /// <summary>
        /// Absolute path to the configuration file (<c>armor.json</c>) within the root directory.
        /// </summary>
        public string ConfigFilePath
        {
            get { return Path.Combine(_RootDirectory, Constants.ConfigFileName); }
        }

        /// <summary>
        /// Absolute path to the log directory within the root directory.
        /// </summary>
        public string LogDirectory
        {
            get { return Path.Combine(_RootDirectory, Constants.LogDirectoryName); }
        }

        /// <summary>
        /// Absolute path to the state directory within the root directory.
        /// </summary>
        public string StateDirectory
        {
            get { return Path.Combine(_RootDirectory, Constants.StateDirectoryName); }
        }

        /// <summary>
        /// Absolute path to the default database file within the root directory. The effective
        /// database path may be overridden by configuration.
        /// </summary>
        public string DefaultDatabasePath
        {
            get { return Path.Combine(_RootDirectory, Constants.DefaultDatabaseFileName); }
        }

        /// <summary>
        /// Initializes a new instance of the <see cref="ArmorPaths"/> class.
        /// </summary>
        /// <param name="rootDirectory">
        /// Explicit root directory. When null, the root is taken from the <c>ARMOR_HOME</c>
        /// environment variable if set, otherwise <c>~/.armor</c>.
        /// </param>
        public ArmorPaths(string? rootDirectory = null)
        {
            if (!String.IsNullOrWhiteSpace(rootDirectory))
            {
                RootDirectory = rootDirectory;
                return;
            }

            string? envRoot = Environment.GetEnvironmentVariable("ARMOR_HOME");
            if (!String.IsNullOrWhiteSpace(envRoot))
            {
                RootDirectory = envRoot;
                return;
            }

            string home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
            if (String.IsNullOrWhiteSpace(home))
                home = Directory.GetCurrentDirectory();

            RootDirectory = Path.Combine(home, Constants.ConfigDirectoryName);
        }

        /// <summary>
        /// Create the root, log, and state directories if they do not already exist. Idempotent.
        /// </summary>
        /// <exception cref="IOException">Thrown when a directory cannot be created.</exception>
        public void EnsureDirectories()
        {
            Directory.CreateDirectory(_RootDirectory);
            Directory.CreateDirectory(LogDirectory);
            Directory.CreateDirectory(StateDirectory);
        }
    }
}
