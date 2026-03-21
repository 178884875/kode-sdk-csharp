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
    bool SupportsToolCalling,
    bool IsDefault,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt,
    int ContextWindowSize = 128_000);
