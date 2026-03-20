namespace KodaClaw.Contracts;

public sealed record ChatStreamRequest(
    string Message,
    string? SessionId = null);
