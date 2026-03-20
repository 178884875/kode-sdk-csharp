using KodaClaw.Contracts;
using Kode.Agent.Sdk.Core.Abstractions;

namespace KodaClaw.Runtime;

public interface IChannelSessionService
{
    Task<ChannelSessionHandle> EnsureChannelSessionAsync(
        ThreadBinding binding,
        ChannelPolicy policy,
        CancellationToken cancellationToken = default);
}

public sealed record ChannelSessionHandle(
    string BindingId,
    string SessionId,
    SessionKind SessionKind,
    string SessionDirectory,
    bool ResumedFromStore,
    string? ResumeFailureMessage,
    IAgent Agent);
