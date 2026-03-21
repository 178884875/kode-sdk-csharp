namespace KodaClaw.ChannelHub;

public interface IChannelConnectorRegistry
{
    Task ReloadAccountAsync(string accountId, CancellationToken cancellationToken = default);

    Task StopAccountAsync(string accountId, CancellationToken cancellationToken = default);
}
