using KodaClaw.Contracts;
using KodaClaw.Runtime;

namespace KodaClaw.ChannelHub;

public sealed record ChannelTurnOrchestrationResult(
    ChannelInboundProcessingResult Processing,
    ChannelTurnOutcome Outcome,
    bool ExecutedTurn,
    ChannelTurnExecutionResult? Execution = null);
