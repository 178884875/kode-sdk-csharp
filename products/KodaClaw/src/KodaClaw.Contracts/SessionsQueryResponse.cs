namespace KodaClaw.Contracts;

public sealed record SessionsQueryResponse(
    IReadOnlyList<SessionSummary> Sessions);
