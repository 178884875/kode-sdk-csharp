namespace KodaClaw.Contracts;

public sealed record RepairChecklist(
    DateTimeOffset GeneratedAt,
    string Scope,
    RepairChecklistSummary Summary,
    IReadOnlyList<RepairChecklistItem> Items);
