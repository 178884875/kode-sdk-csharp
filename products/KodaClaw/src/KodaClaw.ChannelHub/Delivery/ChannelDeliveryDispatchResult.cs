using KodaClaw.Contracts;

namespace KodaClaw.ChannelHub;

public sealed record ChannelDeliveryDispatchResult(
    bool Succeeded,
    ChannelTurnOutcome Outcome,
    string? ErrorMessage = null);
