namespace KodaClaw.Contracts;

public sealed record AutomationRunQuery(
    string? AutomationId = null,
    AutomationRunStatus? Status = null,
    int Limit = 50);
