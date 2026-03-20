namespace KodaClaw.Contracts;

public sealed record SessionDetail(
    string SessionId,
    SessionKind SessionKind,
    SessionStatusSummary Status,
    DateTimeOffset? CreatedAt,
    DateTimeOffset? LastEventAt,
    int UserMessageCount,
    int AssistantMessageCount,
    int ToolCallCount,
    int LastSfpIndex,
    IReadOnlyList<string> PendingApprovalCallIds);
