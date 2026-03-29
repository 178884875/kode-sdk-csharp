using System.Collections.Concurrent;
using KodaClaw.Contracts;
using Microsoft.Extensions.Logging;

namespace KodaClaw.ChannelHub.Connectors.DingTalk;

public sealed class DingTalkConnector : IChannelConnector
{
    private const string DiagnosticSource = "dingtalk";

    private readonly ConcurrentDictionary<string, StartedAccount> _startedAccounts =
        new(StringComparer.Ordinal);

    // conversationId → senderStaffId（用于单聊回复时知道对方的 userId）
    private readonly ConcurrentDictionary<string, string> _conversationUserCache =
        new(StringComparer.Ordinal);

    private readonly IDingTalkApiClient _apiClient;
    private readonly DingTalkConnectorOptions _options;
    private readonly ChannelSecretResolver _secretResolver;
    private readonly IDiagnosticsService? _diagnosticsService;
    private readonly ILogger<DingTalkConnector> _logger;

    public DingTalkConnector(
        ILogger<DingTalkConnector> logger,
        IDingTalkApiClient? apiClient = null,
        DingTalkConnectorOptions? options = null,
        ISecretStore? secretStore = null,
        IDiagnosticsService? diagnosticsService = null)
    {
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _apiClient = apiClient ?? new HttpDingTalkApiClient();
        _options = options ?? new DingTalkConnectorOptions();
        _secretResolver = new ChannelSecretResolver(secretStore);
        _diagnosticsService = diagnosticsService;
    }

    public ChannelConnectorKind Kind => ChannelConnectorKind.DingTalk;

    public Task StartAsync(
        ChannelAccount account,
        Func<ChannelEventEnvelope, CancellationToken, Task> onEvent,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(account);
        ArgumentNullException.ThrowIfNull(onEvent);
        cancellationToken.ThrowIfCancellationRequested();

        if (account.ConnectorKind != ChannelConnectorKind.DingTalk)
        {
            throw new ArgumentException(
                $"DingTalk connector cannot start account with connector kind '{account.ConnectorKind}'.",
                nameof(account));
        }

        var accountId = ValidateAndNormalizeAccountId(account.Id);
        var configuration = DingTalkConnectorConfiguration.FromAccount(account, _secretResolver);

        var streamClient = new DingTalkStreamClient(
            _apiClient,
            configuration.AppKey,
            configuration.AppSecret,
            (eventData, messageId, ct) => DispatchEventAsync(accountId, account, configuration, eventData, messageId, onEvent, ct),
            _options,
            _diagnosticsService);

        var startedAccount = new StartedAccount(
            account: account with { Id = accountId },
            configuration: configuration,
            streamClient: streamClient);

        if (!_startedAccounts.TryAdd(accountId, startedAccount))
        {
            _ = streamClient.DisposeAsync();
            throw new InvalidOperationException($"DingTalk account '{accountId}' is already started.");
        }

        // Fire-and-forget：流连接在后台建立
        streamClient.Start(cancellationToken);

        _logger.LogInformation("DingTalk Stream connector starting for account {AccountId}", accountId);
        RecordDiagnosticEvent("dingtalk.account_started", "info",
            $"DingTalk account started: accountId={accountId}");

        return Task.CompletedTask;
    }

    public async Task StopAsync(string accountId, CancellationToken cancellationToken = default)
    {
        var normalizedAccountId = ValidateAndNormalizeAccountId(accountId);

        if (!_startedAccounts.TryRemove(normalizedAccountId, out var startedAccount))
        {
            return;
        }

        await startedAccount.StreamClient.StopAsync().ConfigureAwait(false);
        await startedAccount.StreamClient.DisposeAsync().ConfigureAwait(false);

        RecordDiagnosticEvent("dingtalk.account_stopped", "info",
            $"DingTalk account stopped: accountId={accountId}");
    }

    public async Task SendAsync(ChannelOutboundDraft draft, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(draft);
        if (draft.ConnectorKind != ChannelConnectorKind.DingTalk)
        {
            throw new ArgumentException(
                $"DingTalk connector cannot send draft with connector kind '{draft.ConnectorKind}'.",
                nameof(draft));
        }

        var accountId = ValidateAndNormalizeAccountId(draft.AccountId);
        if (!_startedAccounts.TryGetValue(accountId, out var startedAccount))
        {
            throw new InvalidOperationException(
                $"DingTalk account '{accountId}' must be started before outbound delivery.");
        }

        if (string.IsNullOrWhiteSpace(draft.ExternalThreadId))
        {
            throw new ArgumentException("DingTalk outbound draft must provide an external thread id.", nameof(draft));
        }

        if (string.IsNullOrWhiteSpace(draft.MessageText))
        {
            throw new ArgumentException("DingTalk outbound draft message text is required.", nameof(draft));
        }

        // ExternalThreadId 格式：conversationId:{id}
        var conversationId = ParseConversationId(draft.ExternalThreadId);

        // 从缓存中查找接收方 userId
        if (!_conversationUserCache.TryGetValue(conversationId, out var recipientUserId)
            || string.IsNullOrWhiteSpace(recipientUserId))
        {
            throw new InvalidOperationException(
                $"DingTalk cannot send to conversation '{conversationId}': recipient userId not cached. " +
                "Wait for the user to send a message first.");
        }

        var accessToken = await _apiClient.GetAccessTokenAsync(
            startedAccount.Configuration.AppKey,
            startedAccount.Configuration.AppSecret,
            cancellationToken).ConfigureAwait(false);

        await _apiClient.SendTextMessageAsync(
            accessToken,
            startedAccount.Configuration.RobotCode,
            [recipientUserId],
            draft.MessageText,
            cancellationToken).ConfigureAwait(false);
    }

    // ── 事件映射 ──────────────────────────────────────────────────────────

    private Task DispatchEventAsync(
        string accountId,
        ChannelAccount account,
        DingTalkConnectorConfiguration configuration,
        DingTalkStreamEventData eventData,
        string messageId,
        Func<ChannelEventEnvelope, CancellationToken, Task> onEvent,
        CancellationToken cancellationToken)
    {
        // 仅处理文本消息
        if (!string.Equals(eventData.MsgType, "text", StringComparison.OrdinalIgnoreCase))
        {
            return Task.CompletedTask;
        }

        var conversationId = eventData.ConversationId;
        if (string.IsNullOrWhiteSpace(conversationId))
        {
            return Task.CompletedTask;
        }

        // 缓存 conversationId → senderStaffId（优先 senderStaffId，降级 senderId）
        var senderUserId = eventData.SenderStaffId ?? eventData.SenderId;
        if (!string.IsNullOrWhiteSpace(senderUserId))
        {
            _conversationUserCache[conversationId] = senderUserId;
        }

        var channelEnvelope = TryMapToEnvelope(accountId, configuration, eventData, messageId);
        if (channelEnvelope is null)
        {
            return Task.CompletedTask;
        }

        return onEvent(channelEnvelope, cancellationToken);
    }

    private static ChannelEventEnvelope? TryMapToEnvelope(
        string accountId,
        DingTalkConnectorConfiguration configuration,
        DingTalkStreamEventData eventData,
        string messageId)
    {
        var rawText = eventData.Text?.Content;
        if (string.IsNullOrWhiteSpace(rawText))
        {
            return null;
        }

        // 钉钉文本内容前面可能有 @ 相关的空格前缀，trim 处理
        var cleanText = rawText.Trim();
        if (string.IsNullOrEmpty(cleanText))
        {
            return null;
        }

        var conversationId = eventData.ConversationId;
        var externalThreadId = $"conversationId:{conversationId}";

        var threadType = ResolveThreadType(eventData.ConversationType);

        var occurredAt = eventData.CreateAt.HasValue
            ? DateTimeOffset.FromUnixTimeMilliseconds(eventData.CreateAt.Value)
            : DateTimeOffset.UtcNow;

        var senderUserId = eventData.SenderStaffId ?? eventData.SenderId ?? string.Empty;

        return new ChannelEventEnvelope(
            EventId: $"dingtalk-{messageId}",
            EventType: ChannelEventType.MessageReceived,
            ConnectorKind: ChannelConnectorKind.DingTalk,
            AccountId: accountId,
            ExternalThreadId: externalThreadId,
            ThreadType: threadType,
            OccurredAt: occurredAt,
            Sender: new ChannelIdentity(Id: senderUserId),
            Recipient: null,
            ExternalMessageId: eventData.MsgId,
            Text: cleanText,
            DefaultDeliveryMode: configuration.DefaultDeliveryMode);
    }

    private static ChannelThreadType ResolveThreadType(string? conversationType)
    {
        // 1 = 单聊, 2 = 群聊
        return string.Equals(conversationType, "1", StringComparison.Ordinal)
            ? ChannelThreadType.DirectMessage
            : ChannelThreadType.Group;
    }

    private static string ParseConversationId(string externalThreadId)
    {
        // 格式：conversationId:{id}
        const string prefix = "conversationId:";
        if (externalThreadId.StartsWith(prefix, StringComparison.Ordinal))
        {
            return externalThreadId[prefix.Length..];
        }

        return externalThreadId;
    }

    private void RecordDiagnosticEvent(string eventType, string level, string message)
    {
        _diagnosticsService?.Record(new DiagnosticEvent(
            Id: Guid.NewGuid().ToString("N"),
            Source: DiagnosticSource,
            EventType: eventType,
            Level: level,
            Message: message,
            Timestamp: DateTimeOffset.UtcNow));
    }

    private static string ValidateAndNormalizeAccountId(string accountId)
    {
        if (string.IsNullOrWhiteSpace(accountId))
        {
            throw new ArgumentException("A non-empty account id is required.", nameof(accountId));
        }

        return accountId.Trim();
    }

    private sealed class StartedAccount
    {
        public StartedAccount(
            ChannelAccount account,
            DingTalkConnectorConfiguration configuration,
            DingTalkStreamClient streamClient)
        {
            Account = account;
            Configuration = configuration;
            StreamClient = streamClient;
        }

        public ChannelAccount Account { get; }
        public DingTalkConnectorConfiguration Configuration { get; }
        public DingTalkStreamClient StreamClient { get; }
    }
}
