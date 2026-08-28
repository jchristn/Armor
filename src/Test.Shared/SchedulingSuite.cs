namespace Test.Shared
{
    using System;
    using System.Collections.Generic;
    using System.Threading.Tasks;
    using Armor.Core.Exceptions;
    using Armor.Core.Models;
    using Armor.Core.Scheduling;
    using Touchstone.Core;

    /// <summary>
    /// Verifies cron parsing and next-occurrence computation, and the cross-process run lock.
    /// </summary>
    public static class SchedulingSuite
    {
        /// <summary>
        /// Build the scheduling test suite.
        /// </summary>
        /// <returns>The scheduling suite descriptor.</returns>
        public static TestSuiteDescriptor Build()
        {
            return new TestSuiteDescriptor(
                suiteId: "Scheduling",
                displayName: "Scheduling and Run Lock",
                cases: new List<TestCaseDescriptor>
                {
                    Sync("CronDaily", "Daily cron computes the next 02:00", () =>
                    {
                        CronSchedule cron = CronSchedule.Parse("0 2 * * *");
                        DateTime after = new DateTime(2026, 1, 1, 3, 0, 0, DateTimeKind.Utc);
                        DateTime? next = cron.NextOccurrenceUtc(after);
                        Check.NotNull(next, "next occurrence computed");
                        Check.Equal(new DateTime(2026, 1, 2, 2, 0, 0, DateTimeKind.Utc), next!.Value, "next is the following day at 02:00");
                    }),

                    Sync("CronEveryFifteen", "Step cron matches every 15 minutes", () =>
                    {
                        CronSchedule cron = CronSchedule.Parse("*/15 * * * *");
                        DateTime after = new DateTime(2026, 1, 1, 10, 3, 0, DateTimeKind.Utc);
                        DateTime? next = cron.NextOccurrenceUtc(after);
                        Check.Equal(new DateTime(2026, 1, 1, 10, 15, 0, DateTimeKind.Utc), next!.Value, "next quarter hour");
                        Check.True(cron.Matches(new DateTime(2026, 1, 1, 10, 30, 0, DateTimeKind.Utc)), "matches on a quarter hour");
                        Check.False(cron.Matches(new DateTime(2026, 1, 1, 10, 31, 0, DateTimeKind.Utc)), "does not match off a quarter hour");
                    }),

                    Sync("CronDayOfWeek", "Day-of-week cron lands on the right weekday", () =>
                    {
                        CronSchedule cron = CronSchedule.Parse("30 9 * * 1");
                        DateTime after = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc);
                        DateTime? next = cron.NextOccurrenceUtc(after);
                        Check.NotNull(next, "next Monday computed");
                        Check.Equal(DayOfWeek.Monday, next!.Value.DayOfWeek, "next occurrence is a Monday");
                        Check.Equal(9, next.Value.Hour, "at 09:00");
                        Check.Equal(30, next.Value.Minute, "at minute 30");
                    }),

                    Sync("CronInvalidThrows", "Invalid cron expressions are rejected", () =>
                    {
                        ExpectArmor(() => CronSchedule.Parse("* * *"), "too few fields");
                        ExpectArmor(() => CronSchedule.Parse("99 * * * *"), "minute out of range");
                        ExpectArmor(() => CronSchedule.Parse("0 0 0 * *"), "day-of-month below range");
                    }),

                    Sync("EvaluatorDueByNextRun", "A schedule is due once its next-run time arrives", () =>
                    {
                        ScheduleEvaluator evaluator = new ScheduleEvaluator();
                        Schedule schedule = new Schedule();
                        schedule.PolicyId = "pol_e";
                        schedule.CronExpression = "0 * * * *";
                        schedule.NextRunUtc = new DateTime(2026, 1, 1, 5, 0, 0, DateTimeKind.Utc);

                        Check.False(evaluator.IsDue(schedule, new DateTime(2026, 1, 1, 4, 59, 0, DateTimeKind.Utc)), "not due before next-run");
                        Check.True(evaluator.IsDue(schedule, new DateTime(2026, 1, 1, 5, 0, 0, DateTimeKind.Utc)), "due at next-run");

                        evaluator.MarkRan(schedule, new DateTime(2026, 1, 1, 5, 0, 0, DateTimeKind.Utc));
                        Check.Equal(new DateTime(2026, 1, 1, 6, 0, 0, DateTimeKind.Utc), schedule.NextRunUtc!.Value, "next run advances one hour");
                        Check.NotNull(schedule.LastRunUtc, "last run recorded");
                    }),

                    Sync("EvaluatorDisabledNeverDue", "A disabled schedule is never due", () =>
                    {
                        ScheduleEvaluator evaluator = new ScheduleEvaluator();
                        Schedule schedule = new Schedule();
                        schedule.PolicyId = "pol_d";
                        schedule.CronExpression = "* * * * *";
                        schedule.Enabled = false;
                        Check.False(evaluator.IsDue(schedule, DateTime.UtcNow), "disabled is not due");
                    }),

                    Case("RunLockExcludes", "Run lock excludes a second holder", async ct =>
                    {
                        using (TempWorkspace ws = new TempWorkspace())
                        {
                            RunLock runLock = new RunLock(ws.Combine("state"));
                            RunLockHandle? first = runLock.TryAcquire("pol_lock");
                            Check.NotNull(first, "first acquire succeeds");

                            RunLockHandle? second = runLock.TryAcquire("pol_lock");
                            Check.Null(second, "second acquire is blocked while held");

                            first!.Dispose();
                            RunLockHandle? third = runLock.TryAcquire("pol_lock");
                            Check.NotNull(third, "acquire succeeds after release");
                            third!.Dispose();
                            await Task.CompletedTask.ConfigureAwait(false);
                        }
                    }),

                    Case("AgentInstanceLockGuardsSingleAgent", "Agent single-instance lock reports and excludes a second agent", async ct =>
                    {
                        using (TempWorkspace ws = new TempWorkspace())
                        {
                            string state = ws.Combine("state");
                            Check.False(AgentInstanceLock.IsRunning(state), "no agent is running before one starts");

                            RunLockHandle? agent = AgentInstanceLock.TryAcquire(state);
                            Check.NotNull(agent, "the first agent acquires the lock");
                            Check.True(AgentInstanceLock.IsRunning(state), "a running agent is detected while it holds the lock");
                            Check.Null(AgentInstanceLock.TryAcquire(state), "a second agent is refused the lock");

                            agent!.Dispose();
                            Check.False(AgentInstanceLock.IsRunning(state), "no agent is detected after it exits");
                            await Task.CompletedTask.ConfigureAwait(false);
                        }
                    })
                });
        }

        private static TestCaseDescriptor Sync(string caseId, string displayName, Action body)
        {
            return new TestCaseDescriptor(suiteId: "Scheduling", caseId: caseId, displayName: displayName, executeAsync: _ =>
            {
                body();
                return Task.CompletedTask;
            });
        }

        private static TestCaseDescriptor Case(string caseId, string displayName, Func<System.Threading.CancellationToken, Task> body)
        {
            return new TestCaseDescriptor(suiteId: "Scheduling", caseId: caseId, displayName: displayName, executeAsync: body);
        }

        private static void ExpectArmor(Action action, string message)
        {
            try
            {
                action();
                throw new InvalidOperationException("Expected ArmorException: " + message);
            }
            catch (ArmorException)
            {
            }
            catch (ArgumentNullException)
            {
            }
        }
    }
}
