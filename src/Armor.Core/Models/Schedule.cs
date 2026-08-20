namespace Armor.Core.Models
{
    using System;
    using Armor.Core.Helpers;

    /// <summary>
    /// A schedule binding a policy to a recurring cron expression. The agent evaluates enabled
    /// schedules on each tick and runs the associated policy when it is due.
    /// </summary>
    public class Schedule
    {
        private string _Id = IdGenerator.GenerateScheduleId();
        private string _PolicyId = String.Empty;
        private string _CronExpression = String.Empty;

        /// <summary>
        /// Unique, K-sortable schedule identifier prefixed with <see cref="Constants.ScheduleIdPrefix"/>.
        /// Defaults to a freshly generated identifier. Cannot be null or whitespace.
        /// </summary>
        /// <exception cref="ArgumentNullException">Thrown when set to null or whitespace.</exception>
        public string Id
        {
            get
            {
                return _Id;
            }
            set
            {
                if (String.IsNullOrWhiteSpace(value))
                    throw new ArgumentNullException(nameof(Id));
                _Id = value;
            }
        }

        /// <summary>
        /// Identifier of the policy this schedule runs. Cannot be null or whitespace.
        /// </summary>
        /// <exception cref="ArgumentNullException">Thrown when set to null or whitespace.</exception>
        public string PolicyId
        {
            get
            {
                return _PolicyId;
            }
            set
            {
                if (String.IsNullOrWhiteSpace(value))
                    throw new ArgumentNullException(nameof(PolicyId));
                _PolicyId = value;
            }
        }

        /// <summary>
        /// Cron expression describing when the policy runs (five-field cron: minute, hour, day of
        /// month, month, day of week). Cannot be null or whitespace.
        /// </summary>
        /// <exception cref="ArgumentNullException">Thrown when set to null or whitespace.</exception>
        public string CronExpression
        {
            get
            {
                return _CronExpression;
            }
            set
            {
                if (String.IsNullOrWhiteSpace(value))
                    throw new ArgumentNullException(nameof(CronExpression));
                _CronExpression = value;
            }
        }

        /// <summary>
        /// Whether the schedule is active. Disabled schedules are ignored by the scheduler. Default
        /// is true.
        /// </summary>
        public bool Enabled { get; set; } = true;

        /// <summary>
        /// UTC timestamp of the last time this schedule triggered a run, or null if it has never run.
        /// </summary>
        public DateTime? LastRunUtc { get; set; } = null;

        /// <summary>
        /// UTC timestamp of the next time this schedule is due, or null if not yet computed.
        /// </summary>
        public DateTime? NextRunUtc { get; set; } = null;

        /// <summary>
        /// UTC timestamp when the schedule was created. Default is the current UTC time.
        /// </summary>
        public DateTime CreatedUtc { get; set; } = DateTime.UtcNow;

        /// <summary>
        /// Initializes a new instance of the <see cref="Schedule"/> class.
        /// </summary>
        public Schedule()
        {
        }
    }
}
