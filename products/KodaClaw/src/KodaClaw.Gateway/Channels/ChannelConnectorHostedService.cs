using KodaClaw.Contracts;

namespace KodaClaw.Gateway.Channels;

internal sealed class ChannelConnectorHostedService : IHostedService
{
    private readonly IChannelAccountRepository _channelAccountRepository;
    private readonly ChannelInboundGatewayService _channelInboundGatewayService;

    public ChannelConnectorHostedService(
        IChannelAccountRepository channelAccountRepository,
        ChannelInboundGatewayService channelInboundGatewayService)
    {
        _channelAccountRepository = channelAccountRepository ?? throw new ArgumentNullException(nameof(channelAccountRepository));
        _channelInboundGatewayService = channelInboundGatewayService ?? throw new ArgumentNullException(nameof(channelInboundGatewayService));
    }

    public async Task StartAsync(CancellationToken cancellationToken)
    {
        var telegramAccounts = await _channelAccountRepository.ListAsync(
            new ChannelAccountQuery(
                ConnectorKind: ChannelConnectorKind.Telegram,
                State: ChannelAccountState.Connected,
                Limit: 200),
            cancellationToken);

        foreach (var account in telegramAccounts.Where(static item => item.InboundEnabled))
        {
            await _channelInboundGatewayService.StartTelegramAccountAsync(account, cancellationToken);
        }
    }

    public async Task StopAsync(CancellationToken cancellationToken)
    {
        var telegramAccounts = await _channelAccountRepository.ListAsync(
            new ChannelAccountQuery(
                ConnectorKind: ChannelConnectorKind.Telegram,
                Limit: 200),
            cancellationToken);

        foreach (var account in telegramAccounts)
        {
            await _channelInboundGatewayService.StopTelegramAccountAsync(account.Id, cancellationToken);
        }
    }
}
