namespace KodaClaw.Contracts;

public sealed record CreateModelEndpointRequest(
    string DisplayName,
    ModelProviderKind Provider,
    string ModelId,
    string? BaseUrl = null,
    string? ApiKeyEnvironmentVariable = null,
    string? ApiKeySecretRef = null,
    bool Enabled = true,
    bool SupportsToolCalling = true,
    int ContextWindowSize = 128_000);
