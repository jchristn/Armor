namespace Armor.Core.Database
{
    using System;
    using System.Collections.Generic;

    /// <summary>
    /// A single, versioned, idempotent schema migration. Migrations run in ascending
    /// <see cref="Version"/> order during initialization and are recorded in the
    /// <c>schema_migrations</c> table so they run at most once.
    /// </summary>
    public class SchemaMigration
    {
        private int _Version = 0;
        private string _Description = String.Empty;
        private List<string> _Statements = new List<string>();

        /// <summary>
        /// Monotonic version number. Must be greater than 0 and unique across migrations. Clamped to
        /// a minimum of 1.
        /// </summary>
        public int Version
        {
            get
            {
                return _Version;
            }
            set
            {
                _Version = value < 1 ? 1 : value;
            }
        }

        /// <summary>
        /// Human-readable description of the migration. Cannot be null or whitespace.
        /// </summary>
        /// <exception cref="ArgumentNullException">Thrown when set to null or whitespace.</exception>
        public string Description
        {
            get
            {
                return _Description;
            }
            set
            {
                if (String.IsNullOrWhiteSpace(value))
                    throw new ArgumentNullException(nameof(Description));
                _Description = value;
            }
        }

        /// <summary>
        /// Ordered SQL statements applied for this migration. Each must be safe to run repeatedly
        /// (for example, <c>CREATE TABLE IF NOT EXISTS</c>). Never null; assigning null replaces it
        /// with an empty list.
        /// </summary>
        public List<string> Statements
        {
            get
            {
                return _Statements;
            }
            set
            {
                _Statements = value ?? new List<string>();
            }
        }

        /// <summary>
        /// Initializes a new instance of the <see cref="SchemaMigration"/> class.
        /// </summary>
        public SchemaMigration()
        {
        }

        /// <summary>
        /// Initializes a new instance of the <see cref="SchemaMigration"/> class.
        /// </summary>
        /// <param name="version">Migration version. Clamped to a minimum of 1.</param>
        /// <param name="description">Migration description. Cannot be null or whitespace.</param>
        /// <param name="statements">Ordered SQL statements. Null becomes an empty list.</param>
        /// <exception cref="ArgumentNullException">Thrown when <paramref name="description"/> is null or whitespace.</exception>
        public SchemaMigration(int version, string description, List<string> statements)
        {
            Version = version;
            Description = description;
            Statements = statements;
        }
    }
}
