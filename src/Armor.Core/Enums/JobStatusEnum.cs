namespace Armor.Core.Enums
{
    /// <summary>
    /// Lifecycle status of a backup or restore job.
    /// </summary>
    public enum JobStatusEnum
    {
        /// <summary>
        /// The job has been created but has not started executing.
        /// </summary>
        Pending,

        /// <summary>
        /// The job is currently executing.
        /// </summary>
        Running,

        /// <summary>
        /// The job finished successfully.
        /// </summary>
        Completed,

        /// <summary>
        /// The job stopped because of an error.
        /// </summary>
        Failed,

        /// <summary>
        /// The job was canceled before it completed.
        /// </summary>
        Canceled
    }
}
