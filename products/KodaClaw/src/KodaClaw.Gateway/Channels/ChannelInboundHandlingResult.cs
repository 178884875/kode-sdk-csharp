using KodaClaw.ChannelHub;

namespace KodaClaw.Gateway.Channels;

internal sealed record ChannelInboundHandlingResult(
    ChannelInboundProcessingResult Processing,
    ChannelTurnOrchestrationResult? Turn = null,
    bool RuntimeExecuted = false,
    string? SkipReason = null);
