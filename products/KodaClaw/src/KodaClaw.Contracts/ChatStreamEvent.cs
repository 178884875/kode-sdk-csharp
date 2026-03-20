namespace KodaClaw.Contracts;

public sealed record ChatStreamEvent(
    string Type,
    string SessionId,
    int? Step = null,
    long? Sequence = null,
    long? Timestamp = null,
    string? Delta = null,
    string? Reason = null,
    ErrorResponse? Error = null);
