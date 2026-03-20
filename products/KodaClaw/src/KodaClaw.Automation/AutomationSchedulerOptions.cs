namespace KodaClaw.Automation;

public sealed class AutomationSchedulerOptions
{
    public bool Enabled { get; set; } = false;

    public TimeSpan PollInterval { get; set; } = TimeSpan.FromMinutes(1);

    public TimeSpan FailureRetryDelay { get; set; } = TimeSpan.FromMinutes(15);
}
