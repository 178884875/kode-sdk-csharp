namespace KodaClaw.Contracts;

public sealed record ModelPreset(
    string PresetId,
    string DisplayName,
    string Provider,
    string ModelId,
    string? BaseUrl,
    int ContextWindowSize,
    string Tier,            // "Recommended" | "Advanced" | "Fast" | "Reasoning" | "Local"
    string Description,
    string? CostHint,
    bool RequiresBaseUrl
);
