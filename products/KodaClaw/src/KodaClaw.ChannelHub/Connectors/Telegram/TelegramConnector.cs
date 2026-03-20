using System.Collections.Concurrent;
using System.Globalization;
using KodaClaw.Contracts;

namespace KodaClaw.ChannelHub.Connectors.Telegram;

public sealed class TelegramConnector : IChannelConnector
{
    private readonly ConcurrentDictionary<string, StartedAccount> _startedAccounts = new(StringComparer.Ordinal);
    private readonly ITelegramApiClient _apiClient;
    private readonly TelegramConnectorOptions _options;
    private readonly ChannelSecretResolver _secretResolver;

    public TelegramConnector(
        ITelegramApiClient? apiClient = null,
        TelegramConnectorOptions? options = null,
        ISecretStore? secretStore = null)
    {
        _apiClient = apiClient ?? new HttpTelegramApiClient();
        _options = options ?? new TelegramConnectorOptions();
        _secretResolver = new ChannelSecretResolver(secretStore);
    }

    public ChannelConnectorKind Kind => ChannelConnectorKind.Telegram;

    public async Task StartAsync(
        ChannelAccount account,
        Func<ChannelEventEnvelope, CancellationToken, Task> onEvent,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(account);
        ArgumentNullException.ThrowIfNull(onEvent);
        cancellationToken.ThrowIfCancellationRequested();

        if (account.ConnectorKind != ChannelConnectorKind.Telegram)
        {
            throw new ArgumentException(
                $"Telegram connector cannot start account with connector kind '{account.ConnectorKind}'.",
                nameof(account));
        }

        var accountId = ValidateAndNormalizeAccountId(account.Id);
        var configuration = TelegramConnectorConfiguration.FromAccount(account, _secretResolver);
        await _apiClient.GetMeAsync(configuration.BotToken, cancellationToken).ConfigureAwait(false);

        var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        var startedAccount = new StartedAccount(
            account: account with { Id = accountId },
            configuration: configuration,
            onEvent: onEvent,
            cancellationTokenSource: linkedCts);
        if (!_startedAccounts.TryAdd(accountId, startedAccount))
        {
            linkedCts.Dispose();
            throw new InvalidOperationException($"Telegram account '{accountId}' is already started.");
        }

        startedAccount.PollingTask = Task.Run(
            () => RunPollingLoopAsync(startedAccount),
            CancellationToken.None);
    }

    public async Task StopAsync(string accountId, CancellationToken cancellationToken = default)
    {
        var normalizedAccountId = ValidateAndNormalizeAccountId(accountId);

        if (!_startedAccounts.TryRemove(normalizedAccountId, out var startedAccount))
        {
            return;
        }

        startedAccount.CancellationTokenSource.Cancel();
        try
        {
            await startedAccount.PollingTask.ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (startedAccount.CancellationTokenSource.IsCancellationRequested)
        {
        }
        finally
        {
            startedAccount.CancellationTokenSource.Dispose();
        }
    }

    public async Task SendAsync(ChannelOutboundDraft draft, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(draft);
        if (draft.ConnectorKind != ChannelConnectorKind.Telegram)
        {
            throw new ArgumentException(
                $"Telegram connector cannot send draft with connector kind '{draft.ConnectorKind}'.",
                nameof(draft));
        }

        var accountId = ValidateAndNormalizeAccountId(draft.AccountId);
        if (!_startedAccounts.TryGetValue(accountId, out var startedAccount))
        {
            throw new InvalidOperationException(
                $"Telegram account '{accountId}' must be started before outbound delivery.");
        }

        if (!long.TryParse(
                draft.ExternalThreadId,
                NumberStyles.Integer,
                CultureInfo.InvariantCulture,
                out var chatId))
        {
            throw new ArgumentException(
                "Telegram outbound draft must provide a numeric external thread id.",
                nameof(draft));
        }

        if (string.IsNullOrWhiteSpace(draft.MessageText))
        {
            throw new ArgumentException("Telegram outbound draft message text is required.", nameof(draft));
        }

        await _apiClient.SendMessageAsync(
            startedAccount.Configuration.BotToken,
            chatId,
            draft.MessageText,
            cancellationToken).ConfigureAwait(false);
    }

    private async Task RunPollingLoopAsync(StartedAccount startedAccount)
    {
        long? nextOffset = null;
        var token = startedAccount.CancellationTokenSource.Token;

        while (!token.IsCancellationRequested)
        {
            try
            {
                var updates = await _apiClient.GetUpdatesAsync(
                    startedAccount.Configuration.BotToken,
                    nextOffset,
                    startedAccount.Configuration.LongPollingTimeoutSeconds,
                    token).ConfigureAwait(false);

                if (updates.Count == 0)
                {
                    await Task.Delay(_options.IdleDelay, token).ConfigureAwait(false);
                    continue;
                }

                foreach (var update in updates.OrderBy(static item => item.UpdateId))
                {
                    var candidateOffset = update.UpdateId + 1;
                    nextOffset = nextOffset.HasValue
                        ? Math.Max(nextOffset.Value, candidateOffset)
                        : candidateOffset;

                    var envelope = TryMapToEnvelope(startedAccount.Account, startedAccount.Configuration, update);
                    if (envelope is null)
                    {
                        continue;
                    }

                    await startedAccount.OnEvent(envelope, token).ConfigureAwait(false);
                }
            }
            catch (OperationCanceledException) when (token.IsCancellationRequested)
            {
                break;
            }
            catch (Exception)
            {
                try
                {
                    await Task.Delay(_options.ErrorRetryDelay, token).ConfigureAwait(false);
                }
                catch (OperationCanceledException) when (token.IsCancellationRequested)
                {
                    break;
                }
            }
        }
    }

    private ChannelEventEnvelope? TryMapToEnvelope(ChannelAccount account, TelegramConnectorConfiguration configuration, TelegramUpdate update)
    {
        if (update is null)
        {
            return null;
        }

        var eventType = default(ChannelEventType?);
        TelegramMessage? message = null;
        if (update.Message is not null)
        {
            eventType = ChannelEventType.MessageReceived;
            message = update.Message;
        }
        else if (update.EditedMessage is not null)
        {
            eventType = ChannelEventType.MessageEdited;
            message = update.EditedMessage;
        }

        if (!eventType.HasValue || message?.Chat is null)
        {
            return null;
        }

        var chat = message.Chat;
        var text = message.GetText();
        if (_options.IgnoreNonTextMessages && string.IsNullOrWhiteSpace(text))
        {
            return null;
        }

        var threadType = ResolveThreadType(chat.Type);
        var occurredAt = message.DateUnixSeconds > 0
            ? DateTimeOffset.FromUnixTimeSeconds(message.DateUnixSeconds)
            : DateTimeOffset.UtcNow;
        var externalThreadId = chat.Id.ToString(CultureInfo.InvariantCulture);
        var externalMessageId = message.MessageId.ToString(CultureInfo.InvariantCulture);

        return new ChannelEventEnvelope(
            EventId: $"telegram-{update.UpdateId.ToString(CultureInfo.InvariantCulture)}",
            EventType: eventType.Value,
            ConnectorKind: ChannelConnectorKind.Telegram,
            AccountId: account.Id,
            ExternalThreadId: externalThreadId,
            ThreadType: threadType,
            OccurredAt: occurredAt,
            Sender: MapSender(message.From),
            Recipient: MapRecipient(chat),
            ExternalMessageId: externalMessageId,
            Text: text,
            DefaultDeliveryMode: configuration.DefaultDeliveryMode);
    }

    private static ChannelThreadType ResolveThreadType(string? chatType)
    {
        return string.Equals(chatType, "private", StringComparison.OrdinalIgnoreCase)
            ? ChannelThreadType.DirectMessage
            : ChannelThreadType.Group;
    }

    private static ChannelIdentity? MapSender(TelegramUser? sender)
    {
        if (sender is null)
        {
            return null;
        }

        var displayName = BuildDisplayName(sender.FirstName, sender.LastName);
        return new ChannelIdentity(
            Id: sender.Id.ToString(CultureInfo.InvariantCulture),
            Username: sender.Username,
            DisplayName: displayName,
            IsBot: sender.IsBot);
    }

    private static ChannelIdentity MapRecipient(TelegramChat chat)
    {
        var displayName = chat.Title ?? chat.Username;
        return new ChannelIdentity(
            Id: chat.Id.ToString(CultureInfo.InvariantCulture),
            Username: chat.Username,
            DisplayName: displayName);
    }

    private static string? BuildDisplayName(string? firstName, string? lastName)
    {
        var normalizedFirst = string.IsNullOrWhiteSpace(firstName) ? null : firstName.Trim();
        var normalizedLast = string.IsNullOrWhiteSpace(lastName) ? null : lastName.Trim();

        if (normalizedFirst is null && normalizedLast is null)
        {
            return null;
        }

        if (normalizedFirst is null)
        {
            return normalizedLast;
        }

        if (normalizedLast is null)
        {
            return normalizedFirst;
        }

        return $"{normalizedFirst} {normalizedLast}";
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
            TelegramConnectorConfiguration configuration,
            Func<ChannelEventEnvelope, CancellationToken, Task> onEvent,
            CancellationTokenSource cancellationTokenSource)
        {
            Account = account;
            Configuration = configuration;
            OnEvent = onEvent;
            CancellationTokenSource = cancellationTokenSource;
        }

        public ChannelAccount Account { get; }

        public TelegramConnectorConfiguration Configuration { get; }

        public Func<ChannelEventEnvelope, CancellationToken, Task> OnEvent { get; }

        public CancellationTokenSource CancellationTokenSource { get; }

        public Task PollingTask { get; set; } = Task.CompletedTask;
    }
}
