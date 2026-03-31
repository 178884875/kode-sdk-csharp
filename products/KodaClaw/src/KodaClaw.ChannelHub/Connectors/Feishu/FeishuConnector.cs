using System.Collections.Concurrent;
using System.Text.Json;
using System.Text.RegularExpressions;
using KodaClaw.Contracts;
using KodaClaw.Workspace;
using Microsoft.Extensions.Logging;

namespace KodaClaw.ChannelHub.Connectors.Feishu;

public sealed class FeishuConnector : IChannelConnector
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    // 飞书 @ 标记格式：<at user_id="xxx">@name</at>
    private static readonly Regex AtTagRegex = new(
        @"<at\s+[^>]*>.*?</at>",
        RegexOptions.Compiled | RegexOptions.IgnoreCase,
        TimeSpan.FromMilliseconds(100));

    private const string DiagnosticSource = "feishu";

    private readonly ConcurrentDictionary<string, StartedAccount> _startedAccounts =
        new(StringComparer.Ordinal);
    private readonly IFeishuApiClient _apiClient;
    private readonly FeishuConnectorOptions _options;
    private readonly ChannelSecretResolver _secretResolver;
    private readonly IMediaStore? _mediaStore;
    private readonly IDiagnosticsService? _diagnosticsService;
    private readonly ILogger<FeishuConnector> _logger;

    public FeishuConnector(
        ILogger<FeishuConnector> logger,
        IFeishuApiClient? apiClient = null,
        FeishuConnectorOptions? options = null,
        ISecretStore? secretStore = null,
        IMediaStore? mediaStore = null,
        IDiagnosticsService? diagnosticsService = null)
    {
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _apiClient = apiClient ?? new HttpFeishuApiClient();
        _options = options ?? new FeishuConnectorOptions();
        _secretResolver = new ChannelSecretResolver(secretStore);
        _mediaStore = mediaStore;
        _diagnosticsService = diagnosticsService;
    }

    public ChannelConnectorKind Kind => ChannelConnectorKind.Feishu;

    public Task StartAsync(
        ChannelAccount account,
        Func<ChannelEventEnvelope, CancellationToken, Task> onEvent,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(account);
        ArgumentNullException.ThrowIfNull(onEvent);
        cancellationToken.ThrowIfCancellationRequested();

        if (account.ConnectorKind != ChannelConnectorKind.Feishu)
        {
            throw new ArgumentException(
                $"Feishu connector cannot start account with connector kind '{account.ConnectorKind}'.",
                nameof(account));
        }

        var accountId = ValidateAndNormalizeAccountId(account.Id);
        var configuration = FeishuConnectorConfiguration.FromAccount(account, _secretResolver);

        var wsClient = new FeishuWebSocketClient(
            _apiClient,
            configuration.AppId,
            configuration.AppSecret,
            (envelope, eventId, ct) => DispatchEventAsync(accountId, account, configuration, envelope, eventId, onEvent, ct),
            _options,
            _diagnosticsService);

        var startedAccount = new StartedAccount(
            account: account with { Id = accountId },
            configuration: configuration,
            wsClient: wsClient);

        if (!_startedAccounts.TryAdd(accountId, startedAccount))
        {
            // 已在运行，清理新建的客户端
            _ = wsClient.DisposeAsync();
            throw new InvalidOperationException($"Feishu account '{accountId}' is already started.");
        }

        // Fire-and-forget：WS 连接在后台建立并自动重连，不阻塞 StartAsync。
        // 连接错误和重连由 FeishuWebSocketClient 内部处理；账号状态更新由调用方（ReconcileChannelAccountRuntimeAsync）
        // 在用户显式重新配置账号时处理。
        wsClient.Start(cancellationToken);

        _logger.LogInformation("Feishu WebSocket connector starting for account {AccountId}", accountId);
        RecordDiagnosticEvent("feishu.account_started", "info",
            $"Feishu account started: accountId={accountId}");

        return Task.CompletedTask;
    }

    public async Task StopAsync(string accountId, CancellationToken cancellationToken = default)
    {
        var normalizedAccountId = ValidateAndNormalizeAccountId(accountId);

        if (!_startedAccounts.TryRemove(normalizedAccountId, out var startedAccount))
        {
            return;
        }

        await startedAccount.WsClient.StopAsync().ConfigureAwait(false);
        await startedAccount.WsClient.DisposeAsync().ConfigureAwait(false);

        RecordDiagnosticEvent("feishu.account_stopped", "info",
            $"Feishu account stopped: accountId={accountId}");
    }

    public async Task SendAsync(ChannelOutboundDraft draft, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(draft);
        if (draft.ConnectorKind != ChannelConnectorKind.Feishu)
        {
            throw new ArgumentException(
                $"Feishu connector cannot send draft with connector kind '{draft.ConnectorKind}'.",
                nameof(draft));
        }

        var accountId = ValidateAndNormalizeAccountId(draft.AccountId);
        if (!_startedAccounts.TryGetValue(accountId, out var startedAccount))
        {
            throw new InvalidOperationException(
                $"Feishu account '{accountId}' must be started before outbound delivery.");
        }

        if (string.IsNullOrWhiteSpace(draft.ExternalThreadId))
        {
            throw new ArgumentException("Feishu outbound draft must provide an external thread id.", nameof(draft));
        }

        if (string.IsNullOrWhiteSpace(draft.MessageText))
        {
            throw new ArgumentException("Feishu outbound draft message text is required.", nameof(draft));
        }

        // ExternalThreadId 格式："{receiveIdType}:{id}"
        // 例如 "chat_id:oc_xxx" 或 "open_id:ou_xxx"
        var (receiveId, receiveIdType) = ParseExternalThreadId(draft.ExternalThreadId);

        var tenantToken = await _apiClient.GetTenantAccessTokenAsync(
            startedAccount.Configuration.AppId,
            startedAccount.Configuration.AppSecret,
            cancellationToken).ConfigureAwait(false);

        var imageAttachment = draft.MediaAttachments?.FirstOrDefault(
            static a => a.ContentType.StartsWith("image/", StringComparison.OrdinalIgnoreCase));

        if (imageAttachment is not null && _mediaStore is not null)
        {
            var stream = await _mediaStore.OpenReadAsync(imageAttachment.MediaId, cancellationToken)
                .ConfigureAwait(false);
            if (stream is not null)
            {
                await using (stream.ConfigureAwait(false))
                {
                    var imageKey = await _apiClient.UploadImageAsync(
                        tenantToken, stream, imageAttachment.ContentType, cancellationToken)
                        .ConfigureAwait(false);

                    await _apiClient.SendImageMessageAsync(
                        tenantToken,
                        receiveId,
                        receiveIdType,
                        imageKey,
                        caption: draft.MessageText,
                        cancellationToken).ConfigureAwait(false);
                }

                return;
            }
        }

        var audioAttachment = draft.MediaAttachments?.FirstOrDefault(
            static a => a.ContentType.StartsWith("audio/", StringComparison.OrdinalIgnoreCase));

        if (audioAttachment is not null && _mediaStore is not null)
        {
            var stream = await _mediaStore.OpenReadAsync(audioAttachment.MediaId, cancellationToken)
                .ConfigureAwait(false);
            if (stream is not null)
            {
                try
                {
                    await using (stream.ConfigureAwait(false))
                    {
                        var fileKey = await _apiClient.UploadAudioFileAsync(
                            tenantToken, stream, audioAttachment.ContentType, cancellationToken)
                            .ConfigureAwait(false);

                        await _apiClient.SendAudioMessageAsync(
                            tenantToken,
                            receiveId,
                            receiveIdType,
                            fileKey,
                            caption: draft.MessageText,
                            cancellationToken).ConfigureAwait(false);
                    }

                    return;
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "Feishu audio upload failed, falling back to text for {ReceiveId}", receiveId);
                    await _apiClient.SendTextMessageAsync(
                        tenantToken, receiveId, receiveIdType,
                        $"[语音消息发送失败，请检查飞书 App 文件上传权限]\n{draft.MessageText}",
                        cancellationToken).ConfigureAwait(false);
                    return;
                }
            }
        }

        var videoAttachment = draft.MediaAttachments?.FirstOrDefault(
            static a => a.ContentType.StartsWith("video/", StringComparison.OrdinalIgnoreCase));

        if (videoAttachment is not null && _mediaStore is not null)
        {
            var stream = await _mediaStore.OpenReadAsync(videoAttachment.MediaId, cancellationToken)
                .ConfigureAwait(false);
            if (stream is not null)
            {
                try
                {
                    await using (stream.ConfigureAwait(false))
                    {
                        // Get duration from media meta if available
                    int? durationMs = videoAttachment.DurationMs;
                    if (durationMs is null)
                    {
                        var meta = await _mediaStore.GetMetaAsync(videoAttachment.MediaId, cancellationToken)
                            .ConfigureAwait(false);
                        durationMs = meta?.DurationMs;
                    }

                    var fileKey = await _apiClient.UploadVideoFileAsync(
                            tenantToken, stream, videoAttachment.ContentType, durationMs, cancellationToken)
                            .ConfigureAwait(false);

                        await _apiClient.SendVideoMessageAsync(
                            tenantToken,
                            receiveId,
                            receiveIdType,
                            fileKey,
                            caption: draft.MessageText,
                            cancellationToken).ConfigureAwait(false);
                    }

                    return;
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "Feishu video upload failed, falling back to text for {ReceiveId}", receiveId);
                    await _apiClient.SendTextMessageAsync(
                        tenantToken, receiveId, receiveIdType,
                        $"[视频消息发送失败，请检查飞书App文件上传权限]\n{draft.MessageText}",
                        cancellationToken).ConfigureAwait(false);
                    return;
                }
            }
        }

        await _apiClient.SendTextMessageAsync(
            tenantToken, receiveId, receiveIdType, draft.MessageText, cancellationToken)
            .ConfigureAwait(false);
    }

    // ── 事件映射 ──────────────────────────────────────────────────────────

    private Task DispatchEventAsync(
        string accountId,
        ChannelAccount account,
        FeishuConnectorConfiguration configuration,
        FeishuWsEventEnvelope envelope,
        string eventId,
        Func<ChannelEventEnvelope, CancellationToken, Task> onEvent,
        CancellationToken cancellationToken)
    {
        var eventType = envelope.Header?.EventType;
        if (!string.Equals(eventType, "im.message.receive_v1", StringComparison.Ordinal))
        {
            return Task.CompletedTask;
        }

        var channelEnvelope = TryMapToEnvelope(accountId, configuration, envelope, eventId);
        if (channelEnvelope is null)
        {
            return Task.CompletedTask;
        }

        return onEvent(channelEnvelope, cancellationToken);
    }

    private ChannelEventEnvelope? TryMapToEnvelope(
        string accountId,
        FeishuConnectorConfiguration configuration,
        FeishuWsEventEnvelope envelope,
        string eventId)
    {
        var message = envelope.Event?.Message;
        var sender = envelope.Event?.Sender;

        if (message is null)
        {
            return null;
        }

        // 仅处理文本消息
        if (!string.Equals(message.MessageType, "text", StringComparison.OrdinalIgnoreCase))
        {
            return null;
        }

        var rawText = ParseTextContent(message.Content);
        var (cleanText, hasMention) = ProcessMentions(rawText, configuration.AppId, message.Mentions);

        var threadType = ResolveThreadType(message.ChatType);
        // 飞书私聊的 ChatId 是 oc_ 开头，属于 chat_id 类型，不是 open_id
        var externalThreadId = $"chat_id:{message.ChatId}";

        var occurredAt = TryParseTimestamp(message.CreateTime);

        return new ChannelEventEnvelope(
            EventId: $"feishu-{eventId}",
            EventType: ChannelEventType.MessageReceived,
            ConnectorKind: ChannelConnectorKind.Feishu,
            AccountId: accountId,
            ExternalThreadId: externalThreadId,
            ThreadType: threadType,
            OccurredAt: occurredAt,
            Sender: MapSender(sender),
            Recipient: null,
            ExternalMessageId: message.MessageId,
            Text: cleanText,
            DefaultDeliveryMode: configuration.DefaultDeliveryMode,
            MetadataJson: hasMention
                ? """{"feishu_has_mention":true}"""
                : null);
    }

    private static (string CleanText, bool HasMention) ProcessMentions(
        string? rawText,
        string appId,
        IReadOnlyList<FeishuMention>? mentions)
    {
        if (string.IsNullOrWhiteSpace(rawText))
        {
            return (string.Empty, false);
        }

        // 检查是否有对本 bot 的 @ 提及
        var hasMention = mentions?.Any(m =>
            string.Equals(m.Id?.OpenId, appId, StringComparison.Ordinal)
            || string.Equals(m.Id?.UnionId, appId, StringComparison.Ordinal)) ?? false;

        // 剥离飞书 @标签（<at ...>@name</at>）
        var clean = AtTagRegex.Replace(rawText, string.Empty).Trim();

        return (clean, hasMention);
    }

    private static string? ParseTextContent(string? contentJson)
    {
        if (string.IsNullOrWhiteSpace(contentJson))
        {
            return null;
        }

        try
        {
            var parsed = JsonSerializer.Deserialize<FeishuTextContent>(contentJson, JsonOptions);
            return parsed?.Text;
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private static ChannelThreadType ResolveThreadType(string? chatType)
    {
        return string.Equals(chatType, "p2p", StringComparison.OrdinalIgnoreCase)
            ? ChannelThreadType.DirectMessage
            : ChannelThreadType.Group;
    }

    private static ChannelIdentity? MapSender(FeishuSender? sender)
    {
        if (sender is null || sender.SenderId is null)
        {
            return null;
        }

        var id = sender.SenderId.OpenId
            ?? sender.SenderId.UnionId
            ?? sender.SenderId.UserId
            ?? string.Empty;

        return new ChannelIdentity(Id: id);
    }

    private static DateTimeOffset TryParseTimestamp(string? createTime)
    {
        if (string.IsNullOrWhiteSpace(createTime))
        {
            return DateTimeOffset.UtcNow;
        }

        // 飞书时间戳是毫秒级 Unix 时间
        if (long.TryParse(createTime, out var ms))
        {
            return DateTimeOffset.FromUnixTimeMilliseconds(ms);
        }

        return DateTimeOffset.UtcNow;
    }

    private static (string ReceiveId, string ReceiveIdType) ParseExternalThreadId(string externalThreadId)
    {
        var separatorIndex = externalThreadId.IndexOf(':', StringComparison.Ordinal);
        if (separatorIndex > 0)
        {
            var type = externalThreadId[..separatorIndex];
            var id = externalThreadId[(separatorIndex + 1)..];
            if (!string.IsNullOrWhiteSpace(type) && !string.IsNullOrWhiteSpace(id))
            {
                return (id, type);
            }
        }

        // 无前缀时默认 open_id
        return (externalThreadId, "open_id");
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
            FeishuConnectorConfiguration configuration,
            FeishuWebSocketClient wsClient)
        {
            Account = account;
            Configuration = configuration;
            WsClient = wsClient;
        }

        public ChannelAccount Account { get; }
        public FeishuConnectorConfiguration Configuration { get; }
        public FeishuWebSocketClient WsClient { get; }
    }
}
