using KodaClaw.Contracts;
using Kode.Agent.Sdk.Core.Abstractions;

namespace KodaClaw.Runtime;

public interface IChannelSessionService
{
    Task<ChannelSessionHandle> EnsureChannelSessionAsync(
        ThreadBinding binding,
        ChannelPolicy policy,
        CancellationToken cancellationToken = default);

    Task<ChannelTurnExecutionResult> RunInboundTurnAsync(
        ThreadBinding binding,
        ChannelPolicy policy,
        ChannelEventEnvelope envelope,
        bool hasExplicitMention,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Evicts the current agent for the given binding and rotates the binding's session ID
    /// so the next inbound turn starts a fresh session (new directory, clean history).
    /// Returns the newly assigned session ID.
    /// </summary>
    Task<string> RotateSessionAsync(ThreadBinding binding, CancellationToken cancellationToken = default);
}

public sealed record ChannelSessionHandle(
    string BindingId,
    string SessionId,
    SessionKind SessionKind,
    string SessionDirectory,
    bool ResumedFromStore,
    string? ResumeFailureMessage,
    IAgent Agent);
