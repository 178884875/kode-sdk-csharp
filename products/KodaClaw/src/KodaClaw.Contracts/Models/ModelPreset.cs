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
    ModelCapabilitySet DefaultCapabilities = ModelCapabilitySet.Text,
    int MaxOutputTokens = 8192,
    bool IsReasoning = false,
    bool SupportsToolCalling = true,
    string? AccessMode = null,          // null = "api"（默认），"coding-plan" = Coding Plan 专属
    string? AnthropicBaseUrl = null     // 该 provider 的 Anthropic 兼容端点（标准 API 协议切换用）
);
