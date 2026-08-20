namespace Armor.Core.Scheduling
{
    using System;
    using System.Collections.Generic;
    using Armor.Core.Exceptions;

    /// <summary>
    /// Parses and evaluates a five-field cron expression (minute, hour, day-of-month, month,
    /// day-of-week). Each field supports <c>*</c>, single values, comma lists, ranges (<c>a-b</c>),
    /// and steps (<c>*/n</c> or <c>a-b/n</c>). Day-of-week uses 0-6 with 0 as Sunday. When both
    /// day-of-month and day-of-week are restricted, a time matches if either matches, following common
    /// cron behavior. This type is immutable after construction and thread-safe.
    /// </summary>
    public sealed class CronSchedule
    {
        private readonly HashSet<int> _Minutes;
        private readonly HashSet<int> _Hours;
        private readonly HashSet<int> _DaysOfMonth;
        private readonly HashSet<int> _Months;
        private readonly HashSet<int> _DaysOfWeek;
        private readonly bool _DomRestricted;
        private readonly bool _DowRestricted;

        private CronSchedule(HashSet<int> minutes, HashSet<int> hours, HashSet<int> daysOfMonth, HashSet<int> months, HashSet<int> daysOfWeek, bool domRestricted, bool dowRestricted)
        {
            _Minutes = minutes;
            _Hours = hours;
            _DaysOfMonth = daysOfMonth;
            _Months = months;
            _DaysOfWeek = daysOfWeek;
            _DomRestricted = domRestricted;
            _DowRestricted = dowRestricted;
        }

        /// <summary>
        /// Parse a five-field cron expression.
        /// </summary>
        /// <param name="expression">The cron expression. Cannot be null or whitespace.</param>
        /// <returns>The parsed schedule.</returns>
        /// <exception cref="ArgumentNullException">Thrown when <paramref name="expression"/> is null or whitespace.</exception>
        /// <exception cref="ArmorException">Thrown when the expression does not have five fields or a field is invalid.</exception>
        public static CronSchedule Parse(string expression)
        {
            if (String.IsNullOrWhiteSpace(expression))
                throw new ArgumentNullException(nameof(expression));

            string[] fields = expression.Trim().Split(new[] { ' ', '\t' }, StringSplitOptions.RemoveEmptyEntries);
            if (fields.Length != 5)
                throw new ArmorException("Cron expression must have exactly five fields but had " + fields.Length + ": '" + expression + "'.");

            HashSet<int> minutes = ParseField(fields[0], 0, 59);
            HashSet<int> hours = ParseField(fields[1], 0, 23);
            HashSet<int> daysOfMonth = ParseField(fields[2], 1, 31);
            HashSet<int> months = ParseField(fields[3], 1, 12);
            HashSet<int> daysOfWeek = ParseField(fields[4], 0, 6);

            bool domRestricted = fields[2].Trim() != "*";
            bool dowRestricted = fields[4].Trim() != "*";

            return new CronSchedule(minutes, hours, daysOfMonth, months, daysOfWeek, domRestricted, dowRestricted);
        }

        /// <summary>
        /// Determine whether a UTC time matches this schedule (to the minute).
        /// </summary>
        /// <param name="utc">The time to test, interpreted as UTC.</param>
        /// <returns>True if the time matches; otherwise false.</returns>
        public bool Matches(DateTime utc)
        {
            if (!_Minutes.Contains(utc.Minute))
                return false;
            if (!_Hours.Contains(utc.Hour))
                return false;
            if (!_Months.Contains(utc.Month))
                return false;

            bool domMatch = _DaysOfMonth.Contains(utc.Day);
            bool dowMatch = _DaysOfWeek.Contains((int)utc.DayOfWeek);

            if (_DomRestricted && _DowRestricted)
                return domMatch || dowMatch;
            if (_DomRestricted)
                return domMatch;
            if (_DowRestricted)
                return dowMatch;
            return true;
        }

        /// <summary>
        /// Compute the next UTC time strictly after the given time that matches this schedule.
        /// </summary>
        /// <param name="afterUtc">The exclusive lower bound, interpreted as UTC.</param>
        /// <returns>The next matching time, or null if none is found within four years.</returns>
        public DateTime? NextOccurrenceUtc(DateTime afterUtc)
        {
            DateTime candidate = new DateTime(afterUtc.Year, afterUtc.Month, afterUtc.Day, afterUtc.Hour, afterUtc.Minute, 0, DateTimeKind.Utc);
            candidate = candidate.AddMinutes(1);

            DateTime limit = candidate.AddYears(4);
            while (candidate < limit)
            {
                if (Matches(candidate))
                    return candidate;
                candidate = candidate.AddMinutes(1);
            }

            return null;
        }

        private static HashSet<int> ParseField(string field, int min, int max)
        {
            HashSet<int> values = new HashSet<int>();
            string trimmed = field.Trim();

            foreach (string part in trimmed.Split(','))
            {
                ParsePart(part.Trim(), min, max, values);
            }

            if (values.Count == 0)
                throw new ArmorException("Cron field '" + field + "' produced no values.");

            return values;
        }

        private static void ParsePart(string part, int min, int max, HashSet<int> values)
        {
            int step = 1;
            string range = part;

            int slashIndex = part.IndexOf('/');
            if (slashIndex >= 0)
            {
                range = part.Substring(0, slashIndex);
                string stepText = part.Substring(slashIndex + 1);
                if (!int.TryParse(stepText, out step) || step <= 0)
                    throw new ArmorException("Invalid cron step in '" + part + "'.");
            }

            int rangeStart;
            int rangeEnd;

            if (range == "*")
            {
                rangeStart = min;
                rangeEnd = max;
            }
            else if (range.Contains('-'))
            {
                string[] bounds = range.Split('-');
                if (bounds.Length != 2 || !int.TryParse(bounds[0], out rangeStart) || !int.TryParse(bounds[1], out rangeEnd))
                    throw new ArmorException("Invalid cron range in '" + part + "'.");
            }
            else
            {
                if (!int.TryParse(range, out rangeStart))
                    throw new ArmorException("Invalid cron value in '" + part + "'.");
                rangeEnd = rangeStart;
            }

            if (rangeStart < min || rangeEnd > max || rangeStart > rangeEnd)
                throw new ArmorException("Cron value out of range in '" + part + "' (allowed " + min + "-" + max + ").");

            for (int value = rangeStart; value <= rangeEnd; value += step)
                values.Add(value);
        }
    }
}
