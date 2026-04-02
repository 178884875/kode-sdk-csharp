using KodaClaw.ChannelHub;
using KodaClaw.ChannelHub.Connectors.Feishu;
using KodaClaw.ChannelHub.Connectors.Telegram;
using KodaClaw.ChannelHub.Connectors.DingTalk;
using KodaClaw.ChannelHub.Connectors.WeChat;
using KodaClaw.ChannelHub.Connectors.Relay;
using KodaClaw.Contracts;
using KodaClaw.Runtime;

namespace KodaClaw.Gateway.Channels;

internal sealed class ChannelInboundGatewayService
{
    private readonly ChannelEventIngestionService _channelEventIngestionService;
    private readonly ChannelTurnOrchestrator _channelTurnOrchestrator;
    private readonly TelegramConnector _telegramConnector;
    private readonly FeishuConnector _feishuConnector;
    private readonly WeChatConnector _weChatConnector;
    private readonly DingTalkConnector _dingTalkConnector;
    private readonly RelayConnector _relayConnector;
    private readonly IRuntimeConfigurationResolver? _runtimeConfigurationResolver;
    private readonly IDiagnosticsService? _diagnosticsService;

    public ChannelInboundGatewayService(
        ChannelEventIngestionService channelEventIngestionService,
        ChannelTurnOrchestrator channelTurnOrchestrator,
        TelegramConnector telegramConnector,
        FeishuConnector feishuConnector,
        WeChatConnector weChatConnector,
        DingTalkConnector dingTalkConnector,
        RelayConnector relayConnector,
        IRuntimeConfigurationResolver? runtimeConfigurationResolver = null,
        IDiagnosticsService? diagnosticsService = null)
    {
        _channelEventIngestionService = channelEventIngestionService ?? throw new ArgumentNullException(nameof(channelEventIngestionService));
        _channelTurnOrchestrator = channelTurnOrchestrator ?? throw new ArgumentNullException(nameof(channelTurnOrchestrator));
        _telegramConnector = telegramConnector ?? throw new ArgumentNullException(nameof(telegramConnector));
        _feishuConnector = feishuConnector ?? throw new ArgumentNullException(nameof(feishuConnector));
        _weChatConnector = weChatConnector ?? throw new ArgumentNullException(nameof(weChatConnector));
        _dingTalkConnector = dingTalkConnector ?? throw new ArgumentNullException(nameof(dingTalkConnector));
        _relayConnector = relayConnector ?? throw new ArgumentNullException(nameof(relayConnector));
        _runtimeConfigurationResolver = runtimeConfigurationResolver;
        _diagnosticsService = diagnosticsService;
    }

    public async Task<ChannelInboundHandlingResult> ProcessAsync(
        ChannelEventEnvelope envelope,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(envelope);

        var runtimeSnapshot = _runtimeConfigurationResolver?.Resolve();
        if (!IsRuntimeSnapshotReady(runtimeSnapshot))
        {
            var processing = await _channelEventIngestionService.IngestAsync(envelope, cancellationToken);
            RecordRuntimeSkipped(processing.Binding, "Skipped channel turn execution because runtime is not configured.");
            return new ChannelInboundHandlingResult(
                Processing: processing,
                RuntimeExecuted: false,
                SkipReason: "runtime_not_configured");
        }

        try
        {
            var orchestration = await _channelTurnOrchestrator.ProcessInboundAsync(envelope, cancellationToken);
            return new ChannelInboundHandlingResult(
                Processing: orchestration.Processing,
                Turn: orchestration,
                RuntimeExecuted: true);
        }
        catch (InvalidOperationException ex) when (LooksLikeRuntimeConfigurationError(ex.Message))
        {
            var processing = await _channelEventIngestionService.IngestAsync(envelope, cancellationToken);
            RecordRuntimeSkipped(processing.Binding, ex.Message);
            return new ChannelInboundHandlingResult(
                Processing: processing,
                RuntimeExecuted: false,
                SkipReason: ex.Message);
        }
    }

    public async Task StartTelegramAccountAsync(
        ChannelAccount account,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(account);
        if (account.ConnectorKind != ChannelConnectorKind.Telegram)
        {
            return;
        }

        try
        {
            await _telegramConnector.StartAsync(
                account,
                async (envelope, token) => await ProcessAsync(envelope, token),
                cancellationToken);
        }
        catch (InvalidOperationException ex) when (ex.Message.Contains("already started", StringComparison.OrdinalIgnoreCase))
        {
            // Account is already live; keep current polling loop.
        }
    }

    public Task StopTelegramAccountAsync(
        string accountId,
        CancellationToken cancellationToken = default)
    {
        return _telegramConnector.StopAsync(accountId, cancellationToken);
    }

    public async Task StartFeishuAccountAsync(
        ChannelAccount account,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(account);
        if (account.ConnectorKind != ChannelConnectorKind.Feishu)
        {
            return;
        }

        RecordChannelEvent("feishu.account_starting", "info",
            $"Starting Feishu account: id={account.Id} inboundEnabled={account.InboundEnabled} state={account.State}",
            account.Id);

        try
        {
            await _feishuConnector.StartAsync(
                account,
                async (envelope, token) => await ProcessAsync(envelope, token),
                cancellationToken);
            RecordChannelEvent("feishu.account_started", "info",
                $"Feishu account started OK: id={account.Id}", account.Id);
        }
        catch (InvalidOperationException ex) when (ex.Message.Contains("already started", StringComparison.OrdinalIgnoreCase))
        {
            // 账号已在运行，保持现有连接
            RecordChannelEvent("feishu.account_already_started", "info",
                $"Feishu account already started: id={account.Id}", account.Id);
        }
        catch (Exception ex)
        {
            RecordChannelEvent("feishu.account_start_failed", "error",
                $"Feishu account start FAILED: id={account.Id} error={ex.Message}", account.Id);
            throw;
        }
    }

    public Task StopFeishuAccountAsync(
        string accountId,
        CancellationToken cancellationToken = default)
    {
        return _feishuConnector.StopAsync(accountId, cancellationToken);
    }

    public async Task StartWeChatAccountAsync(
        ChannelAccount account,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(account);
        if (account.ConnectorKind != ChannelConnectorKind.WeChat)
        {
            return;
        }

        try
        {
            await _weChatConnector.StartAsync(
                account,
                async (envelope, token) => await ProcessAsync(envelope, token),
                cancellationToken);
        }
        catch (InvalidOperationException ex) when (ex.Message.Contains("already started", StringComparison.OrdinalIgnoreCase))
        {
            // 账号已在运行，保持现有轮询
        }
    }

    public Task StopWeChatAccountAsync(
        string accountId,
        CancellationToken cancellationToken = default)
    {
        return _weChatConnector.StopAsync(accountId, cancellationToken);
    }


    public async Task StartDingTalkAccountAsync(
        ChannelAccount account,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(account);
        if (account.ConnectorKind != ChannelConnectorKind.DingTalk)
        {
            return;
        }

        try
        {
            await _dingTalkConnector.StartAsync(
                account,
                async (envelope, token) =>
                    await ProcessAsync(envelope, token),
                cancellationToken);
        }
        catch (InvalidOperationException ex) when (ex.Message.Contains("already started", StringComparison.OrdinalIgnoreCase))
        {
            // Account is already live; keep current connection.
        }
    }

    public Task StopDingTalkAccountAsync(
        string accountId,
        CancellationToken cancellationToken = default)
    {
        return _dingTalkConnector.StopAsync(accountId, cancellationToken);
    }

    public async Task StartRelayAccountAsync(
        ChannelAccount account,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(account);
        if (account.ConnectorKind != ChannelConnectorKind.Relay)
        {
            return;
        }

        try
        {
            await _relayConnector.StartAsync(
                account,
                async (envelope, token) =>
                    await ProcessAsync(envelope, token),
                cancellationToken);
        }
        catch (InvalidOperationException ex) when (ex.Message.Contains("already started", StringComparison.OrdinalIgnoreCase))
        {
            // Account is already live; keep current connection.
        }
    }

    public Task StopRelayAccountAsync(
        string accountId,
        CancellationToken cancellationToken = default)
    {
        return _relayConnector.StopAsync(accountId, cancellationToken);
    }

    private void RecordChannelEvent(string eventType, string level, string message, string? accountId = null)
    {
        _diagnosticsService?.Record(new DiagnosticEvent(
            Id: Guid.NewGuid().ToString("N"),
            Source: "gateway.channels",
            EventType: eventType,
            Level: level,
            Message: message,
            Timestamp: DateTimeOffset.UtcNow,
            Attributes: accountId is not null
                ? new Dictionary<string, string?> { ["accountId"] = accountId }
                : null));
    }

    private void RecordRuntimeSkipped(ThreadBinding binding, string message)
    {
        if (_diagnosticsService is null)
        {
            return;
        }

        _diagnosticsService.Record(new DiagnosticEvent(
            Id: $"diag-channel-runtime-skip-{Guid.NewGuid():N}",
            Source: "gateway.channels",
            EventType: "gateway.channels.session_skipped",
            Level: "warning",
            Message: message,
            Timestamp: DateTimeOffset.UtcNow,
            SessionId: binding.SessionId,
            Attributes: new Dictionary<string, string?>
            {
                ["accountId"] = binding.AccountId,
                ["bindingId"] = binding.Id,
                ["threadType"] = binding.ThreadType.ToString(),
            }));
    }

    private static bool IsRuntimeSnapshotReady(RuntimeConfigurationSnapshot? snapshot)
    {
        if (snapshot is null || string.IsNullOrWhiteSpace(snapshot.DefaultModel))
        {
            return false;
        }

        var hasOpenAi = !string.IsNullOrWhiteSpace(snapshot.OpenAIApiKey);
        var hasAnthropic = !string.IsNullOrWhiteSpace(snapshot.AnthropicApiKey);
        if (!hasOpenAi && !hasAnthropic)
        {
            return false;
        }

        var model = snapshot.DefaultModel!;
        var looksLikeOpenAi = model.StartsWith("gpt", StringComparison.OrdinalIgnoreCase)
            || model.StartsWith("o1", StringComparison.OrdinalIgnoreCase)
            || model.StartsWith("o3", StringComparison.OrdinalIgnoreCase)
            || model.StartsWith("o4", StringComparison.OrdinalIgnoreCase);
        var looksLikeAnthropic = model.StartsWith("claude", StringComparison.OrdinalIgnoreCase);

        if (hasOpenAi && !hasAnthropic)
        {
            return !looksLikeAnthropic;
        }

        if (hasAnthropic && !hasOpenAi)
        {
            return !looksLikeOpenAi;
        }

        return looksLikeOpenAi || looksLikeAnthropic;
    }

    private static bool LooksLikeRuntimeConfigurationError(string? message)
    {
        if (string.IsNullOrWhiteSpace(message))
        {
            return false;
        }

        return message.Contains("KodaClaw chat is not configured", StringComparison.Ordinal)
            || message.Contains("KODACLAW_DEFAULT_MODEL", StringComparison.Ordinal)
            || message.Contains("OPENAI_API_KEY", StringComparison.Ordinal)
            || message.Contains("ANTHROPIC_API_KEY", StringComparison.Ordinal);
    }
}
