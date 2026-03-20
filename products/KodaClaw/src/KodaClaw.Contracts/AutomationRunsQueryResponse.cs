namespace KodaClaw.Contracts;

public sealed record AutomationRunsQueryResponse(
    IReadOnlyList<AutomationRunRecord> Items);
