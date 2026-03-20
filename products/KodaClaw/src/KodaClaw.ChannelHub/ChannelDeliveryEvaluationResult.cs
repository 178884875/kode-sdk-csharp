using KodaClaw.Contracts;

namespace KodaClaw.ChannelHub;

public sealed record ChannelDeliveryEvaluationResult(
    ChannelDeliveryDisposition Disposition,
    DeliveryMode DeliveryMode,
    string? ApprovalId = null,
    string? InboxItemId = null);
