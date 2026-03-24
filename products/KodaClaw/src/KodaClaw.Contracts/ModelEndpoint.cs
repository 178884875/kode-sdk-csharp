namespace KodaClaw.Contracts;

public sealed record ModelEndpoint(
    string Id,
    string DisplayName,
    ModelProviderKind Provider,
    string ModelId,
    string? BaseUrl,
    string? ApiKeyEnvironmentVariable,
    string? ApiKeySecretRef,
    bool Enabled,
    ModelCapabilitySet Capabilities,
    bool IsDefault,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt,
    int ContextWindowSize = 128_000,
    int MaxOutputTokens = 8192,
    bool IsReasoning = false)
{
    /// <summary>向后兼容计算属性，不存入数据库。</summary>
    public bool SupportsToolCalling => Capabilities.HasFlag(ModelCapabilitySet.ToolCalling);
}
