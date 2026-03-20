using System.Text.Json;
using KodaClaw.ChannelHub.Connectors.Telegram;
using KodaClaw.ChannelHub.Connectors.Webhook;
using KodaClaw.Contracts;

namespace KodaClaw.ChannelHub;

public sealed class ChannelDeliveryApprovalService
{
    private const string DeliverySource = "channel.delivery";
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    private readonly IApprovalRepository _approvalRepository;
    private readonly IInboxRepository _inboxRepository;
    private readonly IChannelAccountRepository _channelAccountRepository;
    private readonly IThreadBindingRepository _threadBindingRepository;
    private readonly IChannelAuditRepository? _channelAuditRepository;
    private readonly TelegramConnector _telegramConnector;
    private readonly GenericWebhookConnector _genericWebhookConnector;
    private readonly IDiagnosticsService? _diagnosticsService;
    private readonly ICorrelationContextAccessor? _correlationContextAccessor;

    public ChannelDeliveryApprovalService(
        IApprovalRepository approvalRepository,
        IInboxRepository inboxRepository,
        IChannelAccountRepository channelAccountRepository,
        IThreadBindingRepository threadBindingRepository,
        TelegramConnector telegramConnector,
        GenericWebhookConnector genericWebhookConnector,
        IChannelAuditRepository? channelAuditRepository = null,
        IDiagnosticsService? diagnosticsService = null,
        ICorrelationContextAccessor? correlationContextAccessor = null)
    {
        _approvalRepository = approvalRepository ?? throw new ArgumentNullException(nameof(approvalRepository));
        _inboxRepository = inboxRepository ?? throw new ArgumentNullException(nameof(inboxRepository));
        _channelAccountRepository = channelAccountRepository ?? throw new ArgumentNullException(nameof(channelAccountRepository));
        _threadBindingRepository = threadBindingRepository ?? throw new ArgumentNullException(nameof(threadBindingRepository));
        _telegramConnector = telegramConnector ?? throw new ArgumentNullException(nameof(telegramConnector));
        _genericWebhookConnector = genericWebhookConnector ?? throw new ArgumentNullException(nameof(genericWebhookConnector));
        _channelAuditRepository = channelAuditRepository;
        _diagnosticsService = diagnosticsService;
        _correlationContextAccessor = correlationContextAccessor;
    }

    public Task<ChannelDeliveryApprovalDispatchResult> ApproveAsync(
        Approval approval,
        string? note,
        CancellationToken cancellationToken = default)
    {
        return HandleDecisionAsync(approval, approve: true, note, cancellationToken);
    }

    public Task<ChannelDeliveryApprovalDispatchResult> RejectAsync(
        Approval approval,
        string? note,
        CancellationToken cancellationToken = default)
    {
        return HandleDecisionAsync(approval, approve: false, note, cancellationToken);
    }

    private async Task<ChannelDeliveryApprovalDispatchResult> HandleDecisionAsync(
        Approval approval,
        bool approve,
        string? note,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(approval);

        if (approval.Kind != ApprovalKind.ChannelDelivery)
        {
            return new ChannelDeliveryApprovalDispatchResult(
                ChannelDeliveryApprovalDispatchStatus.InvalidApproval,
                approval,
                "Approval is not a channel delivery approval.");
        }

        if (approval.Status != ApprovalStatus.Pending)
        {
            return new ChannelDeliveryApprovalDispatchResult(
                ChannelDeliveryApprovalDispatchStatus.NotPending,
                approval,
                $"Approval is already {approval.Status}.");
        }

        StoredChannelDeliveryPayload payload;
        ThreadBinding binding;
        ChannelAccount account;

        try
        {
            payload = ParsePayload(approval);
            binding = await LoadBindingAsync(payload.BindingId, cancellationToken);
            account = await LoadAccountAsync(payload.AccountId, cancellationToken);
            ValidatePayloadAgainstBindingAndAccount(payload, binding, account);
        }
        catch (Exception ex) when (ex is JsonException or ArgumentException or InvalidOperationException)
        {
            RecordDiagnosticEvent(
                eventType: "channel.delivery.approval_invalid",
                level: "error",
                message: ex.Message,
                approval: approval,
                binding: null,
                payload: null,
                account: null);

            return new ChannelDeliveryApprovalDispatchResult(
                ChannelDeliveryApprovalDispatchStatus.InvalidApproval,
                approval,
                ex.Message);
        }

        var status = approve ? ApprovalStatus.Approved : ApprovalStatus.Rejected;
        var now = DateTimeOffset.UtcNow;
        var transitioned = await _approvalRepository.TransitionAsync(
            approval.Id,
            status,
            now,
            decidedBy: "api",
            decisionNote: note,
            cancellationToken);

        if (!transitioned)
        {
            var reloaded = await _approvalRepository.GetByIdAsync(approval.Id, cancellationToken);
            return reloaded is null
                ? new ChannelDeliveryApprovalDispatchResult(
                    ChannelDeliveryApprovalDispatchStatus.NotFound,
                    Message: "Approval was not found.")
                : new ChannelDeliveryApprovalDispatchResult(
                    reloaded.Status == ApprovalStatus.Pending
                        ? ChannelDeliveryApprovalDispatchStatus.DeliveryFailed
                        : ChannelDeliveryApprovalDispatchStatus.NotPending,
                    reloaded,
                    reloaded.Status == ApprovalStatus.Pending
                        ? "Timed out waiting for channel delivery approval decision to persist."
                        : $"Approval is already {reloaded.Status}.");
        }

        if (!string.IsNullOrWhiteSpace(approval.InboxItemId))
        {
            await _inboxRepository.UpdateStatusAsync(
                approval.InboxItemId,
                InboxItemStatus.Resolved,
                now,
                resolvedAt: now,
                cancellationToken: cancellationToken);
        }

        var decidedApproval = await _approvalRepository.GetByIdAsync(approval.Id, cancellationToken)
            ?? approval with
            {
                Status = status,
                UpdatedAt = now,
                DecidedAt = now,
                DecidedBy = "api",
                DecisionNote = note,
            };

        await AppendAuditAsync(
            binding,
            payload,
            decidedApproval,
            eventType: approve ? "approval.approved" : "approval.rejected",
            summary: BuildDecisionSummary(approve, payload.MessageText, note),
            createdAt: now,
            cancellationToken);

        RecordDiagnosticEvent(
            eventType: approve
                ? "channel.delivery.approval_approved"
                : "channel.delivery.approval_rejected",
            level: approve ? "info" : "warning",
            message: approve
                ? "Channel delivery approval approved."
                : "Channel delivery approval rejected.",
            approval: decidedApproval,
            binding: binding,
            payload: payload,
            account: account);

        if (!approve)
        {
            return new ChannelDeliveryApprovalDispatchResult(
                ChannelDeliveryApprovalDispatchStatus.Completed,
                decidedApproval);
        }

        try
        {
            await SendAsync(
                account,
                new ChannelOutboundDraft(
                    DraftId: payload.DraftId,
                    BindingId: payload.BindingId,
                    ConnectorKind: payload.ConnectorKind,
                    AccountId: payload.AccountId,
                    ExternalThreadId: payload.ExternalThreadId,
                    MessageText: payload.MessageText,
                    DeliveryMode: payload.DeliveryMode,
                    CreatedAt: now,
                    SessionId: approval.SessionId,
                    CorrelationId: approval.CorrelationId ?? _correlationContextAccessor?.CorrelationId,
                    ApprovalId: approval.Id),
                cancellationToken);
        }
        catch (Exception ex) when (ex is ArgumentException or InvalidOperationException or NotSupportedException)
        {
            await AppendAuditAsync(
                binding,
                payload,
                decidedApproval,
                eventType: "delivery.failed",
                summary: $"Delivery failed: {ex.Message}",
                createdAt: DateTimeOffset.UtcNow,
                cancellationToken);

            RecordDiagnosticEvent(
                eventType: "channel.delivery.failed",
                level: "error",
                message: ex.Message,
                approval: decidedApproval,
                binding: binding,
                payload: payload,
                account: account);

            return new ChannelDeliveryApprovalDispatchResult(
                ChannelDeliveryApprovalDispatchStatus.DeliveryFailed,
                decidedApproval,
                ex.Message);
        }

        var deliveredAt = DateTimeOffset.UtcNow;
        await _threadBindingRepository.UpsertAsync(
            binding with
            {
                UpdatedAt = deliveredAt,
                LastOutboundAt = deliveredAt,
                LastMessagePreview = BuildPreview(payload.MessageText),
            },
            cancellationToken);

        await AppendAuditAsync(
            binding,
            payload,
            decidedApproval,
            eventType: "delivery.sent",
            summary: BuildPreview(payload.MessageText),
            createdAt: deliveredAt,
            cancellationToken);

        RecordDiagnosticEvent(
            eventType: "channel.delivery.sent",
            level: "info",
            message: "Channel delivery completed successfully.",
            approval: decidedApproval,
            binding: binding,
            payload: payload,
            account: account);

        return new ChannelDeliveryApprovalDispatchResult(
            ChannelDeliveryApprovalDispatchStatus.Completed,
            decidedApproval);
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
            // Approval dispatch may run after the connector has already been started by
            // another inbound path; treat that as ready-for-send.
        }
    }

    private async Task AppendAuditAsync(
        ThreadBinding binding,
        StoredChannelDeliveryPayload payload,
        Approval approval,
        string eventType,
        string summary,
        DateTimeOffset createdAt,
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
                CreatedAt: createdAt,
                SessionId: binding.SessionId,
                ApprovalId: approval.Id,
                DeliveryMode: payload.DeliveryMode,
                Summary: summary,
                MetadataJson: BuildAuditMetadata(payload, approval)),
            cancellationToken);
    }

    private void RecordDiagnosticEvent(
        string eventType,
        string level,
        string message,
        Approval approval,
        ThreadBinding? binding,
        StoredChannelDeliveryPayload? payload,
        ChannelAccount? account)
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
            SessionId: approval.SessionId ?? binding?.SessionId,
            CorrelationId: approval.CorrelationId ?? _correlationContextAccessor?.CorrelationId,
            Attributes: new Dictionary<string, string?>
            {
                ["approvalId"] = approval.Id,
                ["bindingId"] = binding?.Id ?? payload?.BindingId,
                ["draftId"] = payload?.DraftId,
                ["connectorKind"] = account?.ConnectorKind.ToString() ?? payload?.ConnectorKind.ToString(),
                ["accountId"] = account?.Id ?? payload?.AccountId,
                ["externalThreadId"] = binding?.ExternalThreadId ?? payload?.ExternalThreadId,
                ["deliveryMode"] = payload?.DeliveryMode.ToString(),
            }));
    }

    private async Task<ThreadBinding> LoadBindingAsync(
        string bindingId,
        CancellationToken cancellationToken)
    {
        var binding = await _threadBindingRepository.GetByIdAsync(bindingId, cancellationToken);
        return binding ?? throw new InvalidOperationException(
            $"Channel thread binding '{bindingId}' was not found.");
    }

    private async Task<ChannelAccount> LoadAccountAsync(
        string accountId,
        CancellationToken cancellationToken)
    {
        var account = await _channelAccountRepository.GetByIdAsync(accountId, cancellationToken);
        return account ?? throw new InvalidOperationException(
            $"Channel account '{accountId}' was not found.");
    }

    private static StoredChannelDeliveryPayload ParsePayload(Approval approval)
    {
        if (string.IsNullOrWhiteSpace(approval.PayloadJson))
        {
            throw new ArgumentException("Channel delivery approval payload is required.", nameof(approval));
        }

        var payload = JsonSerializer.Deserialize<StoredChannelDeliveryPayload>(approval.PayloadJson, JsonOptions);
        if (payload is null)
        {
            throw new JsonException("Channel delivery approval payload is invalid.");
        }

        ValidatePayload(payload);
        return payload;
    }

    private static void ValidatePayload(StoredChannelDeliveryPayload payload)
    {
        ChannelHubValidation.ValidateId(payload.DraftId, nameof(payload.DraftId));
        ChannelHubValidation.ValidateId(payload.BindingId, nameof(payload.BindingId));
        ChannelHubValidation.ValidateId(payload.AccountId, nameof(payload.AccountId));
        ChannelHubValidation.ValidateId(payload.ExternalThreadId, nameof(payload.ExternalThreadId));
        ChannelHubValidation.ValidateId(payload.MessageText, nameof(payload.MessageText));
    }

    private static void ValidatePayloadAgainstBindingAndAccount(
        StoredChannelDeliveryPayload payload,
        ThreadBinding binding,
        ChannelAccount account)
    {
        ChannelHubValidation.ValidateBinding(binding);

        if (!string.Equals(payload.BindingId, binding.Id, StringComparison.Ordinal))
        {
            throw new ArgumentException("Channel delivery approval payload binding id does not match stored binding.");
        }

        if (!string.Equals(payload.AccountId, binding.AccountId, StringComparison.Ordinal) ||
            !string.Equals(payload.AccountId, account.Id, StringComparison.Ordinal))
        {
            throw new ArgumentException("Channel delivery approval payload account id does not match stored channel account.");
        }

        if (payload.ConnectorKind != binding.ConnectorKind || payload.ConnectorKind != account.ConnectorKind)
        {
            throw new ArgumentException("Channel delivery approval payload connector kind does not match stored channel account.");
        }

        if (!string.Equals(payload.ExternalThreadId, binding.ExternalThreadId, StringComparison.Ordinal))
        {
            throw new ArgumentException("Channel delivery approval payload external thread id does not match stored binding.");
        }
    }

    private static string BuildDecisionSummary(bool approved, string messageText, string? note)
    {
        var action = approved ? "Approved" : "Rejected";
        var preview = BuildPreview(messageText);
        return string.IsNullOrWhiteSpace(note)
            ? $"{action}: {preview}"
            : $"{action}: {preview} ({note.Trim()})";
    }

    private static string BuildAuditMetadata(StoredChannelDeliveryPayload payload, Approval approval)
    {
        return JsonSerializer.Serialize(new
        {
            draftId = payload.DraftId,
            bindingId = payload.BindingId,
            connectorKind = payload.ConnectorKind,
            accountId = payload.AccountId,
            externalThreadId = payload.ExternalThreadId,
            deliveryMode = payload.DeliveryMode,
            approvalId = approval.Id,
        }, JsonOptions);
    }

    private static string BuildPreview(string text)
    {
        const int maxLength = 96;

        var normalized = text.Trim().ReplaceLineEndings(" ");
        return normalized.Length <= maxLength
            ? normalized
            : $"{normalized[..maxLength]}...";
    }

    private sealed record StoredChannelDeliveryPayload(
        string DraftId,
        string BindingId,
        ChannelConnectorKind ConnectorKind,
        string AccountId,
        string ExternalThreadId,
        DeliveryMode DeliveryMode,
        string MessageText);
}
