namespace Armor.Core.Database.Sqlite.Implementations
{
    using System;
    using System.Collections.Generic;
    using System.Data;
    using System.Threading;
    using System.Threading.Tasks;
    using Armor.Core.Database.Interfaces;
    using Armor.Core.Enums;
    using Armor.Core.Models;

    /// <summary>
    /// SQLite implementation of <see cref="IGlobalExcludeMethods"/>. The global list lives in the
    /// <c>global_exclude_patterns</c> table as a single ordered set (no policy scope). A replace rewrites
    /// the whole list within one transaction so the stored order matches the supplied order exactly.
    /// </summary>
    public sealed class SqliteGlobalExcludeMethods : IGlobalExcludeMethods
    {
        private readonly DatabaseDriverBase _Driver;

        /// <summary>
        /// Initializes a new instance of the <see cref="SqliteGlobalExcludeMethods"/> class.
        /// </summary>
        /// <param name="driver">The owning database driver. Cannot be null.</param>
        /// <exception cref="ArgumentNullException">Thrown when <paramref name="driver"/> is null.</exception>
        public SqliteGlobalExcludeMethods(DatabaseDriverBase driver)
        {
            _Driver = driver ?? throw new ArgumentNullException(nameof(driver));
        }

        /// <inheritdoc/>
        public async Task<List<ExcludePattern>> ReadAllAsync(CancellationToken token = default)
        {
            DataTable table = await _Driver.ExecuteQueryAsync(
                "SELECT pattern, is_regex, target FROM global_exclude_patterns ORDER BY ordinal ASC;", false, token).ConfigureAwait(false);

            List<ExcludePattern> patterns = new List<ExcludePattern>();
            foreach (DataRow row in table.Rows)
            {
                patterns.Add(new ExcludePattern(
                    Converters.GetString(row, "pattern"),
                    Converters.GetBool(row, "is_regex"),
                    Converters.GetEnum<ExcludeTargetEnum>(row, "target", ExcludeTargetEnum.Any)));
            }
            return patterns;
        }

        /// <inheritdoc/>
        public async Task ReplaceAllAsync(IEnumerable<ExcludePattern> patterns, CancellationToken token = default)
        {
            List<string> queries = new List<string> { "DELETE FROM global_exclude_patterns;" };
            queries.AddRange(BuildInserts(patterns));
            await _Driver.ExecuteQueriesAsync(queries, true, token).ConfigureAwait(false);
        }

        /// <inheritdoc/>
        public async Task<List<ExcludePattern>> ResetToDefaultsAsync(CancellationToken token = default)
        {
            List<ExcludePattern> defaults = GlobalExcludeDefaults.Create();
            await ReplaceAllAsync(defaults, token).ConfigureAwait(false);
            return defaults;
        }

        private static List<string> BuildInserts(IEnumerable<ExcludePattern> patterns)
        {
            List<string> queries = new List<string>();
            if (patterns == null)
                return queries;

            int ordinal = 0;
            foreach (ExcludePattern pattern in patterns)
            {
                if (pattern == null)
                    continue;
                queries.Add("INSERT INTO global_exclude_patterns (ordinal, pattern, is_regex, target) VALUES (" +
                    Sanitizer.Int(ordinal) + ", " +
                    Sanitizer.Literal(pattern.Pattern) + ", " +
                    Sanitizer.Bool(pattern.IsRegex) + ", " +
                    Sanitizer.Literal(pattern.Target.ToString()) + ");");
                ordinal++;
            }
            return queries;
        }
    }
}
