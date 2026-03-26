namespace KodaClaw.Contracts;

public sealed record DiagnosticsQuery(
    int Limit = 50,
    string? CorrelationId = null,
    string? SessionId = null,
    string? Source = null,
    string? EventType = null,
    string? Level = null,
    DateTimeOffset? DateFrom = null,
    DateTimeOffset? DateTo = null);
