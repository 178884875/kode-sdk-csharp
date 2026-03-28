namespace KodaClaw.Automation;

public interface IOneShotTimerService
{
    Task TickAsync(CancellationToken cancellationToken = default);
}
