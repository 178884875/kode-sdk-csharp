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
    bool RequiresBaseUrl,
    ModelCapabilitySet DefaultCapabilities = ModelCapabilitySet.TextChat | ModelCapabilitySet.ToolCalling,
    int MaxOutputTokens = 8192,
    bool IsReasoning = false
);
