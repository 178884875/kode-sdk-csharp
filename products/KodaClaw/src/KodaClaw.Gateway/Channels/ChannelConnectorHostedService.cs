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
        await StartAccountsByKindAsync(ChannelConnectorKind.Telegram, cancellationToken);
        await StartAccountsByKindAsync(ChannelConnectorKind.Feishu, cancellationToken);
    }

    public async Task StopAsync(CancellationToken cancellationToken)
    {
        IReadOnlyList<ChannelAccount> allAccounts;
        try
        {
            allAccounts = await _channelAccountRepository.ListAsync(
                new ChannelAccountQuery(Limit: 200),
                cancellationToken);
        }
        catch (Exception ex)
        {
            // Repository may be unavailable during shutdown (e.g. after a backup import
            // replaced the database file). Best-effort: log and return.
            _logger.LogWarning(ex, "Failed to list channel accounts during shutdown; skipping connector stop.");
            return;
        }

        foreach (var account in allAccounts)
        {
            try
            {
                await StopAccountByKindAsync(account.Id, account.ConnectorKind, cancellationToken);
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

        var account = await _channelAccountRepository.GetByIdAsync(accountId, cancellationToken);
        if (account is null)
        {
            _logger.LogWarning("Channel account '{AccountId}' not found during reload.", accountId);
            return;
        }

        // Stop first (best-effort) then re-start.
        try
        {
            await StopAccountByKindAsync(accountId, account.ConnectorKind, cancellationToken);
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

        if (!account.InboundEnabled || account.State != ChannelAccountState.Connected)
        {
            return;
        }

        try
        {
            await StartAccountByKindAsync(account, cancellationToken);
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

        var account = await _channelAccountRepository.GetByIdAsync(accountId, cancellationToken);
        var kind = account?.ConnectorKind ?? ChannelConnectorKind.Telegram;

        try
        {
            await StopAccountByKindAsync(accountId, kind, cancellationToken);
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

    private async Task StartAccountsByKindAsync(
        ChannelConnectorKind kind,
        CancellationToken cancellationToken)
    {
        var accounts = await _channelAccountRepository.ListAsync(
            new ChannelAccountQuery(
                ConnectorKind: kind,
                State: ChannelAccountState.Connected,
                Limit: 200),
            cancellationToken);

        foreach (var account in accounts.Where(static item => item.InboundEnabled))
        {
            try
            {
                await StartAccountByKindAsync(account, cancellationToken);
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

    private Task StartAccountByKindAsync(ChannelAccount account, CancellationToken cancellationToken)
    {
        return account.ConnectorKind switch
        {
            ChannelConnectorKind.Telegram =>
                _channelInboundGatewayService.StartTelegramAccountAsync(account, cancellationToken),
            ChannelConnectorKind.Feishu =>
                _channelInboundGatewayService.StartFeishuAccountAsync(account, cancellationToken),
            _ => Task.CompletedTask,
        };
    }

    private Task StopAccountByKindAsync(
        string accountId,
        ChannelConnectorKind kind,
        CancellationToken cancellationToken)
    {
        return kind switch
        {
            ChannelConnectorKind.Telegram =>
                _channelInboundGatewayService.StopTelegramAccountAsync(accountId, cancellationToken),
            ChannelConnectorKind.Feishu =>
                _channelInboundGatewayService.StopFeishuAccountAsync(accountId, cancellationToken),
            _ => Task.CompletedTask,
        };
    }
}
