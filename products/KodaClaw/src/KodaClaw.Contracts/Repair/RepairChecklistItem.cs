namespace KodaClaw.Contracts;

public sealed record RepairChecklistItem(
    string Id,
    RepairChecklistSeverity Severity,
    RepairChecklistState State,
    string Category,
    string Title,
    string Summary,
    string? Resource = null,
    string? Action = null,
    string? Evidence = null);
