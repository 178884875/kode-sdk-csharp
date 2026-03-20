namespace KodaClaw.Contracts;

public sealed record AutomationDefinition(
    string Id,
    string Title,
    string Prompt,
    AutomationDefinitionSource Source,
    string? SourcePath,
    AutomationSchedule Schedule,
    bool Enabled,
    IReadOnlyList<string>? InputPaths,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt,
    DateTimeOffset? LastRunAt,
    DateTimeOffset? NextRunAt,
    AutomationRunStatus? LastRunStatus,
    string? LastError);
