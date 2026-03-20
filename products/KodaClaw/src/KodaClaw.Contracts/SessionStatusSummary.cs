namespace KodaClaw.Contracts;

public sealed record SessionStatusSummary(
    bool IsActiveMainSession,
    string? BreakpointState,
    int MessageCount,
    int PendingApprovalCount);
