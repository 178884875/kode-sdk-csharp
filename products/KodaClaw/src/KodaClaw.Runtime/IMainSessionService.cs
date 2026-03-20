using KodaClaw.Contracts;
using Kode.Agent.Sdk.Core.Abstractions;

namespace KodaClaw.Runtime;

public interface IMainSessionService
{
    Task<MainSessionHandle> EnsureMainSessionAsync(CancellationToken cancellationToken = default);

    Task<ApprovalDecisionDispatchResult> ApproveApprovalAsync(
        string approvalId,
        CancellationToken cancellationToken = default);

    Task<ApprovalDecisionDispatchResult> RejectApprovalAsync(
        string approvalId,
        string? note = null,
        CancellationToken cancellationToken = default);
}

public sealed record MainSessionHandle(
    string SessionId,
    SessionKind SessionKind,
    string SessionDirectory,
    bool ResumedFromStore,
    string? ResumeFailureMessage,
    IAgent Agent);
