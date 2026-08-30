namespace Armor.Core.Diagnostics
{
    using System;
    using System.Globalization;
    using Armor.Core.Models;

    /// <summary>
    /// Formats a finished <see cref="BackupJob"/> into short, human-readable summary text shared by the
    /// terminal UI and the tray agent, so a run's completion reads the same in the activity log, in the
    /// completion modal, and in a desktop notification.
    /// </summary>
    public static class BackupJobSummary
    {
        /// <summary>
        /// A one-line summary of a finished run: files captured, bytes written to the target, and the
        /// new-versus-reused chunk split.
        /// </summary>
        /// <param name="job">The finished job. Cannot be null.</param>
        /// <returns>The summary text.</returns>
        /// <exception cref="ArgumentNullException">Thrown when <paramref name="job"/> is null.</exception>
        public static string OneLine(BackupJob job)
        {
            if (job == null)
                throw new ArgumentNullException(nameof(job));

            return job.FileCount.ToString("N0", CultureInfo.InvariantCulture) + " files, "
                + FormatBytes(job.BytesWritten) + " written, "
                + job.ChunksWritten.ToString("N0", CultureInfo.InvariantCulture) + " new / "
                + job.ChunksReused.ToString("N0", CultureInfo.InvariantCulture) + " reused chunks";
        }

        /// <summary>
        /// Formats a byte count with a binary unit suffix (B, KB, MB, GB, TB, PB), one decimal place above
        /// bytes.
        /// </summary>
        /// <param name="bytes">The number of bytes.</param>
        /// <returns>The formatted string.</returns>
        public static string FormatBytes(long bytes)
        {
            string[] units = { "B", "KB", "MB", "GB", "TB", "PB" };
            double value = bytes;
            int unit = 0;
            while (value >= 1024 && unit < units.Length - 1)
            {
                value /= 1024;
                unit++;
            }
            return unit == 0
                ? bytes.ToString(CultureInfo.InvariantCulture) + " B"
                : value.ToString("0.0", CultureInfo.InvariantCulture) + " " + units[unit];
        }
    }
}
