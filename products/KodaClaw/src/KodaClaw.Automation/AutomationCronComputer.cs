using Cronos;

namespace KodaClaw.Automation;

/// <summary>
/// Shared cron-schedule computation helper used by both the scheduler
/// and the API layer to derive the next-run time from a cron expression.
/// </summary>
public static class AutomationCronComputer
{
    /// <summary>
    /// Returns the next occurrence of <paramref name="cronExpression"/> strictly
    /// after <paramref name="after"/>, evaluated in the local machine timezone
    /// so that "0 9 * * *" means 09:00 in the user's own timezone.
    /// </summary>
    public static DateTimeOffset ComputeNextRunAt(string cronExpression, DateTimeOffset after)
    {
        try
        {
            var cron = CronExpression.Parse(cronExpression.Trim());
            var tz = TimeZoneInfo.Local;
            var next = cron.GetNextOccurrence(after, tz);
            return next ?? after.AddHours(1);
        }
        catch (CronFormatException)
        {
            return after.AddHours(1);
        }
    }
}
