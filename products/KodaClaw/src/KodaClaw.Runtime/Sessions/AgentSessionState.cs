using Kode.Agent.Sdk.Core.Abstractions;

namespace KodaClaw.Runtime;

/// <summary>
/// Lightweight runtime state snapshot for a channel session agent.
/// </summary>
public sealed record AgentSessionState(
    string SessionId,
    AgentRuntimeState RuntimeState,
    BreakpointState BreakpointState,
    int StepCount,
    string? CurrentToolName);
