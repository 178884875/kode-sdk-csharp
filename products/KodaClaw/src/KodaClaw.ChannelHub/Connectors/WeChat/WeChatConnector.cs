using System.Collections.Concurrent;
using System.Text.Json;
using System.Text.RegularExpressions;
using KodaClaw.Contracts;
using KodaClaw.Workspace;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace KodaClaw.ChannelHub.Connectors.WeChat;

public sealed class WeChatConnector : IChannelConnector
{
    private const int ILinkErrCodeSessionExpired = -14;
    private const int ILinkMessageTypeText = 1;

    // 正则：去除常见 Markdown 标记，微信不支持 Markdown
    private static readonly Regex MarkdownRegex = new(
        @"(\*\*|__)(.*?)\1|(\*|_)(.*?)\3|#{1,6}\s+|`{1,3}[^`]*`{1,3}|>\s+|!\[.*?\]\(.*?\)|\[([^\]]+)\]\([^)]+\)",
        RegexOptions.Compiled | RegexOptions.Singleline,
        TimeSpan.FromMilliseconds(200));

    private readonly ConcurrentDictionary<string, StartedAccount> _startedAccounts =
        new(StringComparer.Ordinal);

    // key = "accountId::fromUserId"，value = 最近一次收到的 contextToken
    // 发送回复时使用，避免 ThreadBinding 存储 contextToken 带来的 schema 变更
    private readonly ConcurrentDictionary<string, string> _contextTokenCache =
        new(StringComparer.Ordinal);

    private readonly IWeChatApiClient _apiClient;
    private readonly WeChatAuthManager _authManager;
    private readonly WeChatConnectorOptions _options;
    private readonly ChannelSecretResolver _secretResolver;
    private readonly IChannelAccountRepository? _accountRepository;
    private readonly ILogger<WeChatConnector> _logger;
    private readonly string _workspaceRootPath;

    public WeChatConnector(
        IWeChatApiClient apiClient,
        WeChatAuthManager authManager,
        KodaClawWorkspaceOptions workspaceOptions,
        ILogger<WeChatConnector> logger,
        WeChatConnectorOptions? options = null,
        ISecretStore? secretStore = null,
        IChannelAccountRepository? accountRepository = null)
    {
        _apiClient = apiClient;
        _authManager = authManager;
        _workspaceRootPath = workspaceOptions.ResolveRootPath();
        _logger = logger;
        _options = options ?? new WeChatConnectorOptions();
        _secretResolver = new ChannelSecretResolver(secretStore);
        _accountRepository = accountRepository;
    }

    public ChannelConnectorKind Kind => ChannelConnectorKind.WeChat;

    public Task StartAsync(
        ChannelAccount account,
        Func<ChannelEventEnvelope, CancellationToken, Task> onEvent,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(account);
        ArgumentNullException.ThrowIfNull(onEvent);
        cancellationToken.ThrowIfCancellationRequested();

        if (account.ConnectorKind != ChannelConnectorKind.WeChat)
        {
            throw new ArgumentException(
                $"WeChat connector cannot start account with connector kind '{account.ConnectorKind}'.",
                nameof(account));
        }

        var accountId = ValidateAndNormalizeAccountId(account.Id);
        var configuration = WeChatConnectorConfiguration.FromAccount(account, _workspaceRootPath, _secretResolver);

        // 设置 API Client 的 BotToken
        _apiClient.SetBotToken(configuration.BotToken);

        // 加载持久化游标
        var syncBuf = _authManager.LoadSyncBuf(configuration.StateDir);

        var cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        var startedAccount = new StartedAccount(
            Account: account with { Id = accountId },
            Configuration: configuration,
            Cts: cts,
            SyncBuf: syncBuf);

        if (!_startedAccounts.TryAdd(accountId, startedAccount))
        {
            cts.Dispose();
            throw new InvalidOperationException($"WeChat account '{accountId}' is already started.");
        }

        // 启动后台长轮询，不等待
        _logger.LogInformation("WeChat connector starting poll loop for account {AccountId}", accountId);

        _ = Task.Run(
            () => PollLoopAsync(startedAccount, onEvent, cts.Token),
            CancellationToken.None);

        return Task.CompletedTask;
    }

    public async Task StopAsync(string accountId, CancellationToken cancellationToken = default)
    {
        var normalizedAccountId = ValidateAndNormalizeAccountId(accountId);

        if (!_startedAccounts.TryRemove(normalizedAccountId, out var startedAccount))
            return;

        await startedAccount.Cts.CancelAsync().ConfigureAwait(false);
        startedAccount.Cts.Dispose();
    }

    public Task SendAsync(ChannelOutboundDraft draft, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(draft);
        if (draft.ConnectorKind != ChannelConnectorKind.WeChat)
        {
            throw new ArgumentException(
                $"WeChat connector cannot send draft with connector kind '{draft.ConnectorKind}'.",
                nameof(draft));
        }

        var accountId = ValidateAndNormalizeAccountId(draft.AccountId);
        if (!_startedAccounts.TryGetValue(accountId, out var startedAccount))
        {
            throw new InvalidOperationException(
                $"WeChat account '{accountId}' must be started before outbound delivery.");
        }

        if (string.IsNullOrWhiteSpace(draft.ExternalThreadId))
            throw new ArgumentException("WeChat outbound draft must provide an external thread id.", nameof(draft));

        if (string.IsNullOrWhiteSpace(draft.MessageText))
            throw new ArgumentException("WeChat outbound draft message text is required.", nameof(draft));

        // 微信个人号不支持音频发送（需 AMR 格式转码），明确拒绝
        var audioAttachment = draft.MediaAttachments?.FirstOrDefault(
            static a => a.ContentType.StartsWith("audio/", StringComparison.OrdinalIgnoreCase));
        if (audioAttachment is not null)
            throw new NotSupportedException("微信个人号不支持发送音频文件");

        // 优先从内存缓存读取 contextToken（收到对方消息时缓存，持续有效直到 Gateway 重启）
        // 缓存未命中时回退到 MetadataJson（如 channel_send 工具直接调用时）
        var cacheKey = $"{accountId}::{draft.ExternalThreadId}";
        var contextToken = _contextTokenCache.TryGetValue(cacheKey, out var cached)
            ? cached
            : ReadContextToken(draft.MetadataJson);
        _logger.LogInformation(
            "WeChat SendAsync: accountId={AccountId} toUserId={ToUserId} contextTokenLen={Len}",
            accountId, draft.ExternalThreadId, contextToken.Length);

        // 微信不支持 Markdown，剥离标记转为纯文本
        var plainText = MarkdownToPlainText(draft.MessageText);

        return _apiClient.SendTextAsync(
            draft.ExternalThreadId,
            contextToken,
            plainText,
            cancellationToken);
    }

    // ── 长轮询主循环 ──────────────────────────────────────────

    private async Task PollLoopAsync(
        StartedAccount ctx,
        Func<ChannelEventEnvelope, CancellationToken, Task> onEvent,
        CancellationToken ct)
    {
        _logger.LogInformation("WeChat poll loop started for account {AccountId}", ctx.Account.Id);
        try
        {
        while (!ct.IsCancellationRequested)
        {
            try
            {
                var response = await _apiClient.GetUpdatesAsync(ctx.SyncBuf, ct).ConfigureAwait(false);

                if (response.Ret == ILinkErrCodeSessionExpired)
                {
                    _logger.LogWarning("WeChat session expired for account {AccountId}, marking degraded", ctx.Account.Id);
                    await MarkAccountDegradedAsync(ctx.Account, "iLink session expired (errcode -14). Re-login required.", ct)
                        .ConfigureAwait(false);
                    await Task.Delay(_options.SessionExpiredRetryDelayMs, ct).ConfigureAwait(false);
                    continue;
                }

                // 更新游标
                if (!string.IsNullOrEmpty(response.GetUpdatesBuf))
                {
                    ctx.SyncBuf = response.GetUpdatesBuf;
                    _authManager.SaveSyncBuf(ctx.Configuration.StateDir, ctx.SyncBuf);
                }

                if (response.Msgs.Count > 0)
                    _logger.LogInformation("WeChat received {Count} message(s) for account {AccountId}", response.Msgs.Count, ctx.Account.Id);

                foreach (var msg in response.Msgs)
                {
                    // LRU 去重
                    if (ctx.SeenMessageIds.Contains(msg.MessageId))
                        continue;
                    ctx.SeenMessageIds.Add(msg.MessageId);
                    if (ctx.SeenMessageIds.Count > _options.LruDeduplicationSize)
                        ctx.SeenMessageIds.RemoveAt(0);

                    // 缓存 contextToken，供后续回复时使用
                    if (!string.IsNullOrEmpty(msg.ContextToken))
                    {
                        var cacheKey = $"{ctx.Account.Id}::{msg.FromUserId}";
                        _contextTokenCache[cacheKey] = msg.ContextToken;
                    }

                    foreach (var item in msg.ItemList)
                    {
                        if (item.Type != ILinkMessageTypeText || string.IsNullOrWhiteSpace(item.TextItem?.Text))
                            continue;

                        var envelope = BuildEnvelope(ctx.Account, msg, item.TextItem.Text, ctx.Configuration);
                        await SendTypingAndProcessAsync(msg.FromUserId, msg.ContextToken, envelope, onEvent, ct)
                            .ConfigureAwait(false);
                    }
                }
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "WeChat poll error for account {AccountId}, retrying in {DelayMs}ms", ctx.Account.Id, _options.ErrorRetryDelayMs);
                try { await Task.Delay(_options.ErrorRetryDelayMs, ct).ConfigureAwait(false); }
                catch (OperationCanceledException) { break; }
            }
        }
        }
        finally
        {
            // loop 退出时（无论正常还是异常）移除自己，允许 StartAsync 重新添加
            _startedAccounts.TryRemove(ctx.Account.Id, out _);
            _logger.LogInformation("WeChat poll loop stopped for account {AccountId}", ctx.Account.Id);
        }
    }

    // ── 正在输入 ──────────────────────────────────────────────

    private async Task SendTypingAndProcessAsync(
        string fromUserId,
        string contextToken,
        ChannelEventEnvelope envelope,
        Func<ChannelEventEnvelope, CancellationToken, Task> onEvent,
        CancellationToken ct)
    {
        // 尝试获取 typing_ticket；失败时直接处理消息（typing 是可选特性）
        string? typingTicket = null;
        if (!string.IsNullOrEmpty(contextToken))
        {
            try
            {
                var config = await _apiClient.GetConfigAsync(fromUserId, contextToken, ct).ConfigureAwait(false);
                if (config.Ret == 0 && !string.IsNullOrEmpty(config.TypingTicket))
                    typingTicket = config.TypingTicket;
            }
            catch (Exception ex)
            {
                _logger.LogDebug(ex, "WeChat getconfig failed for {UserId}, skipping typing indicator", fromUserId);
            }
        }

        if (typingTicket is null)
        {
            await onEvent(envelope, ct).ConfigureAwait(false);
            return;
        }

        // 发送"正在输入"并在 Agent 处理期间每 5 秒续发一次
        try { await _apiClient.SendTypingAsync(fromUserId, typingTicket, 1, ct).ConfigureAwait(false); }
        catch (Exception ex) { _logger.LogDebug(ex, "WeChat sendtyping(1) failed"); }

        using var typingCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        var typingLoop = Task.Run(async () =>
        {
            while (!typingCts.Token.IsCancellationRequested)
            {
                try { await Task.Delay(5_000, typingCts.Token).ConfigureAwait(false); }
                catch (OperationCanceledException) { break; }
                try { await _apiClient.SendTypingAsync(fromUserId, typingTicket, 1, typingCts.Token).ConfigureAwait(false); }
                catch { /* best-effort */ }
            }
        }, CancellationToken.None);

        try
        {
            await onEvent(envelope, ct).ConfigureAwait(false);
        }
        finally
        {
            await typingCts.CancelAsync().ConfigureAwait(false);
            try { await typingLoop.ConfigureAwait(false); } catch { }
            // 取消输入中状态（用 None，不受 ct 影响）
            try { await _apiClient.SendTypingAsync(fromUserId, typingTicket, 2, CancellationToken.None).ConfigureAwait(false); }
            catch (Exception ex) { _logger.LogDebug(ex, "WeChat sendtyping(2) failed"); }
        }
    }

    // ── 事件构造 ─────────────────────────────────────────────

    private static ChannelEventEnvelope BuildEnvelope(
        ChannelAccount account,
        ILinkMessage msg,
        string text,
        WeChatConnectorConfiguration configuration)
    {
        var metadataJson = JsonSerializer.Serialize(new { contextToken = msg.ContextToken });

        return new ChannelEventEnvelope(
            EventId: $"wechat-{msg.MessageId}",
            EventType: ChannelEventType.MessageReceived,
            ConnectorKind: ChannelConnectorKind.WeChat,
            AccountId: account.Id,
            ExternalThreadId: msg.FromUserId,
            ThreadType: ChannelThreadType.DirectMessage,
            OccurredAt: DateTimeOffset.UtcNow,
            Sender: new ChannelIdentity(msg.FromUserId, null, msg.FromUserId),
            Recipient: null,
            ExternalMessageId: msg.MessageId.ToString(),
            Text: text,
            MetadataJson: metadataJson,
            DefaultDeliveryMode: configuration.DefaultDeliveryMode);
    }

    // ── 账号状态管理 ─────────────────────────────────────────

    private async Task MarkAccountDegradedAsync(ChannelAccount account, string error, CancellationToken ct)
    {
        if (_accountRepository is null) return;

        var degraded = account with
        {
            State = ChannelAccountState.Degraded,
            LastError = error
        };

        await _accountRepository.UpsertAsync(degraded, ct).ConfigureAwait(false);
    }

    // ── 工具方法 ─────────────────────────────────────────────

    private static string ReadContextToken(string? metadataJson)
    {
        if (string.IsNullOrWhiteSpace(metadataJson)) return string.Empty;

        try
        {
            using var doc = JsonDocument.Parse(metadataJson);
            if (doc.RootElement.TryGetProperty("contextToken", out var prop)
                && prop.ValueKind == JsonValueKind.String)
                return prop.GetString() ?? string.Empty;
        }
        catch { /* 忽略 */ }

        return string.Empty;
    }

    internal static string MarkdownToPlainText(string text)
    {
        if (string.IsNullOrEmpty(text)) return text;

        // 替换匹配到的 Markdown 标记：保留文字内容，去掉标记符号
        var result = MarkdownRegex.Replace(text, match =>
        {
            // 加粗/斜体：保留内容组
            if (match.Groups[2].Success) return match.Groups[2].Value;
            if (match.Groups[4].Success) return match.Groups[4].Value;
            // 链接：保留链接文字
            if (match.Groups[5].Success) return match.Groups[5].Value;
            // 标题/引用/代码块：去掉标记，保留空字符串（后续 trim）
            return string.Empty;
        });

        return result.Trim();
    }

    private static string ValidateAndNormalizeAccountId(string accountId)
    {
        if (string.IsNullOrWhiteSpace(accountId))
            throw new ArgumentException("Account ID must not be empty.", nameof(accountId));
        return accountId.Trim();
    }

    // ── 内部状态 ─────────────────────────────────────────────

    private sealed class StartedAccount(
        ChannelAccount Account,
        WeChatConnectorConfiguration Configuration,
        CancellationTokenSource Cts,
        string SyncBuf)
    {
        public ChannelAccount Account { get; } = Account;
        public WeChatConnectorConfiguration Configuration { get; } = Configuration;
        public CancellationTokenSource Cts { get; } = Cts;
        public string SyncBuf { get; set; } = SyncBuf;

        /// <summary>内存 LRU 消息 ID 列表（有序，最早加入的在前）</summary>
        public List<long> SeenMessageIds { get; } = new();
    }
}
