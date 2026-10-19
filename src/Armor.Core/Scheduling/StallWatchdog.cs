namespace Armor.Core.Scheduling
{
    using System;
    using System.Threading;
    using System.Threading.Tasks;
    using Armor.Core.Exceptions;

    /// <summary>
    /// Runs a long operation under an inactivity watchdog. The operation receives a cancellation token and
    /// a heartbeat to call whenever it makes progress; if no heartbeat arrives for the stall timeout, the
    /// watchdog cancels the operation and throws <see cref="BackupStalledException"/>. An operation blocked
    /// somewhere cancellation cannot reach (a synchronous read the operating system never returns from) is
    /// abandoned after a short grace period rather than awaited forever, so the caller always regains
    /// control. This type is stateless and thread-safe.
    /// </summary>
    public static class StallWatchdog
    {
        private static readonly TimeSpan MaxPollInterval = TimeSpan.FromSeconds(30);

        /// <summary>
        /// Run an operation, failing it if it stops reporting progress.
        /// </summary>
        /// <typeparam name="T">The operation's result type.</typeparam>
        /// <param name="operation">Starts the operation, given a cancellation token and a heartbeat action. Cannot be null.</param>
        /// <param name="stallTimeout">How long the operation may go without a heartbeat. Must be positive.</param>
        /// <param name="token">Cancellation token for the caller (for example shutdown).</param>
        /// <returns>The operation's result.</returns>
        /// <exception cref="ArgumentNullException">Thrown when <paramref name="operation"/> is null.</exception>
        /// <exception cref="ArgumentOutOfRangeException">Thrown when <paramref name="stallTimeout"/> is not positive.</exception>
        /// <exception cref="BackupStalledException">Thrown when the operation goes a full stall timeout without a heartbeat.</exception>
        /// <exception cref="OperationCanceledException">Thrown when <paramref name="token"/> is canceled.</exception>
        public static async Task<T> RunAsync<T>(Func<CancellationToken, Action, Task<T>> operation, TimeSpan stallTimeout, CancellationToken token = default)
        {
            if (operation == null)
                throw new ArgumentNullException(nameof(operation));
            if (stallTimeout <= TimeSpan.Zero)
                throw new ArgumentOutOfRangeException(nameof(stallTimeout), "Stall timeout must be positive.");

            TimeSpan poll = stallTimeout / 4 < MaxPollInterval ? stallTimeout / 4 : MaxPollInterval;
            ActivityClock clock = new ActivityClock();
            CancellationTokenSource linked = CancellationTokenSource.CreateLinkedTokenSource(token);
            Task<T> work;
            try
            {
                work = operation(linked.Token, clock.Touch);
            }
            catch
            {
                linked.Dispose();
                throw;
            }

            bool abandoned = false;
            try
            {
                while (true)
                {
                    Task finished = await Task.WhenAny(work, Task.Delay(poll, CancellationToken.None)).ConfigureAwait(false);
                    if (finished == work)
                        return await work.ConfigureAwait(false);

                    if (token.IsCancellationRequested)
                    {
                        // Shutdown: the operation shares the token, so give it a moment to unwind cleanly.
                        abandoned = !await CompletesWithinAsync(work, poll).ConfigureAwait(false);
                        if (abandoned)
                            Observe(work);
                        else
                            await SwallowAsync(work).ConfigureAwait(false);
                        token.ThrowIfCancellationRequested();
                    }

                    if (clock.IdleFor < stallTimeout)
                        continue;

                    linked.Cancel();
                    string message = "The backup made no progress for " + Describe(stallTimeout) + " and was stopped.";
                    if (!await CompletesWithinAsync(work, poll).ConfigureAwait(false))
                    {
                        abandoned = true;
                        Observe(work);
                        throw new BackupStalledException(message + " It did not respond to cancellation and was abandoned; restart Armor to release it.");
                    }

                    // It stopped. If it happened to finish in the same instant, keep the result.
                    if (work.IsCompletedSuccessfully)
                        return work.Result;
                    Exception? inner = work.Exception?.GetBaseException();
                    if (inner != null && !(inner is OperationCanceledException))
                        throw new BackupStalledException(message, inner);
                    throw new BackupStalledException(message);
                }
            }
            finally
            {
                // An abandoned operation may still observe the token, so leave its source alive.
                if (!abandoned)
                    linked.Dispose();
            }
        }

        private static async Task<bool> CompletesWithinAsync(Task work, TimeSpan wait)
        {
            Task finished = await Task.WhenAny(work, Task.Delay(wait, CancellationToken.None)).ConfigureAwait(false);
            return finished == work;
        }

        private static async Task SwallowAsync(Task task)
        {
            try
            {
                await task.ConfigureAwait(false);
            }
            catch
            {
                // The caller is already propagating its own outcome.
            }
        }

        private static void Observe(Task task)
        {
            // Observe an abandoned task's eventual fault so it is never reported as unobserved.
            _ = task.ContinueWith(t => _ = t.Exception, CancellationToken.None, TaskContinuationOptions.OnlyOnFaulted | TaskContinuationOptions.ExecuteSynchronously, TaskScheduler.Default);
        }

        private static string Describe(TimeSpan span)
        {
            if (span.TotalMinutes >= 1)
                return Math.Round(span.TotalMinutes) + " minute(s)";
            return Math.Round(span.TotalSeconds, 1) + " second(s)";
        }

        private sealed class ActivityClock
        {
            private long _LastTick = Environment.TickCount64;

            internal TimeSpan IdleFor
            {
                get { return TimeSpan.FromMilliseconds(Environment.TickCount64 - Interlocked.Read(ref _LastTick)); }
            }

            internal void Touch()
            {
                Interlocked.Exchange(ref _LastTick, Environment.TickCount64);
            }
        }
    }
}
