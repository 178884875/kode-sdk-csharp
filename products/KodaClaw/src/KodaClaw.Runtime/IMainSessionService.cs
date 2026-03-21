using KodaClaw.Contracts;
using Kode.Agent.Sdk.Core.Abstractions;

namespace KodaClaw.Runtime;

public interface IMainSessionService
{
    Task<MainSessionHandle> EnsureMainSessionAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Disposes the current main session and clears the active session pointer,
    /// so the next call to <see cref="EnsureMainSessionAsync"/> creates a fresh session.
    /// Workspace memory files (MEMORY.md, daily logs, etc.) are not affected.
    /// </summary>
    /// <returns>The session ID of the session that was rotated out, or null if no active session existed.</returns>
    Task<string?> RotateMainSessionAsync(CancellationToken cancellationToken = default);

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
