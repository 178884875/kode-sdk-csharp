namespace KodaClaw.Contracts;

public sealed record ChannelOutboundDraft(
    string DraftId,
    string BindingId,
    ChannelConnectorKind ConnectorKind,
    string AccountId,
    string ExternalThreadId,
    string MessageText,
    DeliveryMode DeliveryMode,
    DateTimeOffset CreatedAt,
    string? SessionId = null,
    string? CorrelationId = null,
    string? ApprovalId = null,
    string? MetadataJson = null,
    IReadOnlyList<MediaReference>? MediaAttachments = null);
