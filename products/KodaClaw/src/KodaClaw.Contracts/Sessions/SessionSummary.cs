namespace KodaClaw.Contracts;

public sealed record SessionSummary(
    string SessionId,
    SessionKind SessionKind,
    SessionStatusSummary Status,
    DateTimeOffset? CreatedAt,
    DateTimeOffset? LastEventAt,
    string? Title = null);
