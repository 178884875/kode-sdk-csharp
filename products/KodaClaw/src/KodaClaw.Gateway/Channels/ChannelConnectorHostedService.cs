using KodaClaw.ChannelHub;
using KodaClaw.Contracts;

namespace KodaClaw.Gateway.Channels;

internal sealed class ChannelConnectorHostedService : IHostedService, IChannelConnectorRegistry
{
    private readonly IChannelAccountRepository _channelAccountRepository;
    private readonly ChannelInboundGatewayService _channelInboundGatewayService;
    private readonly ILogger<ChannelConnectorHostedService> _logger;

    public ChannelConnectorHostedService(
        IChannelAccountRepository channelAccountRepository,
        ChannelInboundGatewayService channelInboundGatewayService,
        ILogger<ChannelConnectorHostedService> logger)
    {
        _channelAccountRepository = channelAccountRepository ?? throw new ArgumentNullException(nameof(channelAccountRepository));
        _channelInboundGatewayService = channelInboundGatewayService ?? throw new ArgumentNullException(nameof(channelInboundGatewayService));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
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
            try
            {
                await _channelInboundGatewayService.StartTelegramAccountAsync(account, cancellationToken);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex,
                    "Channel account '{AccountId}' failed to start; skipping.",
                    account.Id);
            }
        }
    }

    public async Task StopAsync(CancellationToken cancellationToken)
    {
        IReadOnlyList<ChannelAccount> telegramAccounts;
        try
        {
            telegramAccounts = await _channelAccountRepository.ListAsync(
                new ChannelAccountQuery(
                    ConnectorKind: ChannelConnectorKind.Telegram,
                    Limit: 200),
                cancellationToken);
        }
        catch (Exception ex)
        {
            // Repository may be unavailable during shutdown (e.g. after a backup import
            // replaced the database file). Best-effort: log and return.
            _logger.LogWarning(ex, "Failed to list channel accounts during shutdown; skipping connector stop.");
            return;
        }

        foreach (var account in telegramAccounts)
        {
            try
            {
                await _channelInboundGatewayService.StopTelegramAccountAsync(account.Id, cancellationToken);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex,
                    "Channel account '{AccountId}' failed to stop cleanly.",
                    account.Id);
            }
        }
    }

    public async Task ReloadAccountAsync(string accountId, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(accountId);

        // Stop first (best-effort) then re-start.
        try
        {
            await _channelInboundGatewayService.StopTelegramAccountAsync(accountId, cancellationToken);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex,
                "Channel account '{AccountId}' failed to stop before reload; continuing with start.",
                accountId);
        }

        var account = await _channelAccountRepository.GetByIdAsync(accountId, cancellationToken);
        if (account is null)
        {
            _logger.LogWarning("Channel account '{AccountId}' not found during reload.", accountId);
            return;
        }

        if (!account.InboundEnabled || account.State != ChannelAccountState.Connected)
        {
            return;
        }

        try
        {
            await _channelInboundGatewayService.StartTelegramAccountAsync(account, cancellationToken);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex,
                "Channel account '{AccountId}' failed to start after reload.",
                accountId);
        }
    }

    public async Task StopAccountAsync(string accountId, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(accountId);

        try
        {
            await _channelInboundGatewayService.StopTelegramAccountAsync(accountId, cancellationToken);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex,
                "Channel account '{AccountId}' failed to stop.",
                accountId);
        }
    }
}
