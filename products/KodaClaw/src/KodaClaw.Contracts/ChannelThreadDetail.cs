namespace KodaClaw.Contracts;

public sealed record ChannelThreadDetail(
    ChannelAccount Account,
    ThreadBinding Binding,
    ChannelPolicy Policy,
    DeliveryRule DeliveryRule,
    IReadOnlyList<ChannelAuditEntry> RecentAudit,
    SessionSummary? Session = null,
    string? PendingApprovalId = null,
    bool HasPendingDraft = false);
