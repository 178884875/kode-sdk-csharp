namespace KodaClaw.Contracts;

public sealed record RepairChecklistSummary(
    int TotalCount,
    int BlockingCount,
    int ActionRequiredCount,
    int WarningCount,
    int InfoCount);
