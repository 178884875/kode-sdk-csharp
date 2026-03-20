using Kode.Agent.Sdk.Core.Abstractions;

namespace KodaClaw.Runtime;

public sealed record ChannelTurnExecutionResult(
    ChannelSessionHandle Session,
    AgentRunResult RunResult,
    string RawResponse,
    ChannelReplyProposal Proposal,
    bool HasExplicitMention);
