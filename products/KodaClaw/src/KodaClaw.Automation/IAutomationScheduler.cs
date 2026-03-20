namespace KodaClaw.Automation;

public interface IAutomationScheduler
{
    Task<int> TickAsync(CancellationToken cancellationToken = default);

    Task<int> RunOnceAsync(CancellationToken cancellationToken = default);
}
