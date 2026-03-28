using KodaClaw.Contracts;

namespace KodaClaw.ChannelHub;

public sealed record ChannelInboundProcessingResult(
    ThreadBinding Binding,
    ChannelPolicy Policy,
    DeliveryRule DeliveryRule,
    bool CreatedBinding,
    ChannelAuditEntry? AuditEntry = null);
