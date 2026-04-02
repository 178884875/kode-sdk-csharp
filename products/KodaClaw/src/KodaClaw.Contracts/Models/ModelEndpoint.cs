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
    bool IsReasoning = false,
    IReadOnlyDictionary<string, string>? CustomHeaders = null);
