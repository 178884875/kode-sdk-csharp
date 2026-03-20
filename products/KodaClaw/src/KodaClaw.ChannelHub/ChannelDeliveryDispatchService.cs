using System.Text.Json;
using KodaClaw.ChannelHub.Connectors.Telegram;
using KodaClaw.ChannelHub.Connectors.Webhook;
using KodaClaw.Contracts;

namespace KodaClaw.ChannelHub;

public sealed class ChannelDeliveryDispatchService
{
    private const string DeliverySource = "channel.delivery";
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    private readonly IThreadBindingRepository _threadBindingRepository;
    private readonly IChannelAuditRepository? _channelAuditRepository;
    private readonly TelegramConnector _telegramConnector;
    private readonly GenericWebhookConnector _genericWebhookConnector;
    private readonly IDiagnosticsService? _diagnosticsService;
    private readonly ICorrelationContextAccessor? _correlationContextAccessor;

    public ChannelDeliveryDispatchService(
        IThreadBindingRepository threadBindingRepository,
        TelegramConnector telegramConnector,
        GenericWebhookConnector genericWebhookConnector,
        IChannelAuditRepository? channelAuditRepository = null,
        IDiagnosticsService? diagnosticsService = null,
        ICorrelationContextAccessor? correlationContextAccessor = null)
    {
        _threadBindingRepository = threadBindingRepository ?? throw new ArgumentNullException(nameof(threadBindingRepository));
        _telegramConnector = telegramConnector ?? throw new ArgumentNullException(nameof(telegramConnector));
        _genericWebhookConnector = genericWebhookConnector ?? throw new ArgumentNullException(nameof(genericWebhookConnector));
        _channelAuditRepository = channelAuditRepository;
        _diagnosticsService = diagnosticsService;
        _correlationContextAccessor = correlationContextAccessor;
    }

    public async Task<ChannelDeliveryDispatchResult> DispatchAsync(
        ChannelAccount account,
        ThreadBinding binding,
        ChannelOutboundDraft draft,
        ChannelTurnOutcome intendedOutcome,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(account);
        ChannelHubValidation.ValidateBinding(binding);
        ArgumentNullException.ThrowIfNull(draft);
        ArgumentNullException.ThrowIfNull(intendedOutcome);

        try
        {
            await SendAsync(account, draft, cancellationToken);

            var deliveredAt = DateTimeOffset.UtcNow;
            await _threadBindingRepository.UpsertAsync(
                binding with
                {
                    UpdatedAt = deliveredAt,
                    LastOutboundAt = deliveredAt,
                    LastMessagePreview = BuildPreview(draft.MessageText),
                },
                cancellationToken);

            var deliveredOutcome = intendedOutcome with
            {
                Kind = ChannelTurnOutcomeKind.Delivered,
                Summary = string.IsNullOrWhiteSpace(intendedOutcome.Summary)
                    ? BuildPreview(draft.MessageText)
                    : intendedOutcome.Summary,
                OccurredAt = deliveredAt,
                ReplyText = draft.MessageText,
                DeliveryMode = draft.DeliveryMode,
                DraftId = draft.DraftId,
            };

            await AppendAuditAsync(
                binding,
                eventType: "delivery.sent",
                summary: BuildPreview(draft.MessageText),
                outcome: deliveredOutcome,
                cancellationToken);

            RecordDiagnosticEvent(
                eventType: "channel.delivery.sent",
                level: "info",
                message: "Channel delivery completed successfully.",
                binding: binding,
                draft: draft,
                outcome: deliveredOutcome,
                errorMessage: null);

            return new ChannelDeliveryDispatchResult(
                Succeeded: true,
                Outcome: deliveredOutcome);
        }
        catch (Exception ex) when (ex is ArgumentException or InvalidOperationException or NotSupportedException)
        {
            var failedAt = DateTimeOffset.UtcNow;
            var failedOutcome = intendedOutcome with
            {
                Kind = ChannelTurnOutcomeKind.Failed,
                Summary = $"Delivery failed: {ex.Message}",
                OccurredAt = failedAt,
                ReplyText = draft.MessageText,
                DeliveryMode = draft.DeliveryMode,
                DraftId = draft.DraftId,
            };

            await AppendAuditAsync(
                binding,
                eventType: "delivery.failed",
                summary: failedOutcome.Summary,
                outcome: failedOutcome,
                cancellationToken);

            RecordDiagnosticEvent(
                eventType: "channel.delivery.failed",
                level: "error",
                message: ex.Message,
                binding: binding,
                draft: draft,
                outcome: failedOutcome,
                errorMessage: ex.Message);

            return new ChannelDeliveryDispatchResult(
                Succeeded: false,
                Outcome: failedOutcome,
                ErrorMessage: ex.Message);
        }
    }

    private async Task SendAsync(
        ChannelAccount account,
        ChannelOutboundDraft draft,
        CancellationToken cancellationToken)
    {
        switch (account.ConnectorKind)
        {
            case ChannelConnectorKind.Telegram:
                await EnsureTelegramStartedAsync(account, cancellationToken);
                await _telegramConnector.SendAsync(draft, cancellationToken);
                return;
            case ChannelConnectorKind.GenericWebhook:
                await _genericWebhookConnector.SendAsync(draft, cancellationToken);
                return;
            default:
                throw new NotSupportedException(
                    $"Connector '{account.ConnectorKind}' does not support channel delivery dispatch.");
        }
    }

    private async Task EnsureTelegramStartedAsync(
        ChannelAccount account,
        CancellationToken cancellationToken)
    {
        try
        {
            await _telegramConnector.StartAsync(
                account,
                static (_, _) => Task.CompletedTask,
                cancellationToken);
        }
        catch (InvalidOperationException)
        {
            // Already started by another inbound path.
        }
    }

    private async Task AppendAuditAsync(
        ThreadBinding binding,
        string eventType,
        string summary,
        ChannelTurnOutcome outcome,
        CancellationToken cancellationToken)
    {
        if (_channelAuditRepository is null)
        {
            return;
        }

        await _channelAuditRepository.AppendAsync(
            new ChannelAuditEntry(
                Id: $"audit-{Guid.NewGuid():N}",
                BindingId: binding.Id,
                ConnectorKind: binding.ConnectorKind,
                AccountId: binding.AccountId,
                ExternalThreadId: binding.ExternalThreadId,
                ThreadType: binding.ThreadType,
                EventType: eventType,
                CreatedAt: outcome.OccurredAt,
                SessionId: binding.SessionId,
                ApprovalId: outcome.ApprovalId,
                DeliveryMode: outcome.DeliveryMode,
                Summary: summary,
                MetadataJson: JsonSerializer.Serialize(outcome, JsonOptions)),
            cancellationToken);
    }

    private void RecordDiagnosticEvent(
        string eventType,
        string level,
        string message,
        ThreadBinding binding,
        ChannelOutboundDraft draft,
        ChannelTurnOutcome outcome,
        string? errorMessage)
    {
        if (_diagnosticsService is null)
        {
            return;
        }

        _diagnosticsService.Record(new DiagnosticEvent(
            Id: $"diag-channel-delivery-{Guid.NewGuid():N}",
            Source: DeliverySource,
            EventType: eventType,
            Level: level,
            Message: message,
            Timestamp: DateTimeOffset.UtcNow,
            SessionId: binding.SessionId,
            CorrelationId: draft.CorrelationId ?? _correlationContextAccessor?.CorrelationId,
            Attributes: new Dictionary<string, string?>
            {
                ["bindingId"] = binding.Id,
                ["draftId"] = draft.DraftId,
                ["connectorKind"] = draft.ConnectorKind.ToString(),
                ["accountId"] = draft.AccountId,
                ["externalThreadId"] = draft.ExternalThreadId,
                ["deliveryMode"] = draft.DeliveryMode.ToString(),
                ["approvalId"] = outcome.ApprovalId,
                ["inboxItemId"] = outcome.InboxItemId,
                ["outcomeKind"] = outcome.Kind.ToString(),
                ["error"] = errorMessage,
            }));
    }

    private static string BuildPreview(string text)
    {
        const int maxLength = 96;

        var normalized = text.Trim();
        if (normalized.Length <= maxLength)
        {
            return normalized;
        }

        return $"{normalized[..maxLength]}...";
    }
}
