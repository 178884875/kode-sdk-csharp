namespace KodaClaw.Contracts;

public sealed record UpdateModelEndpointRequest(
    string DisplayName,
    ModelProviderKind Provider,
    string ModelId,
    string? BaseUrl = null,
    string? ApiKeyEnvironmentVariable = null,
    string? ApiKeySecretRef = null,
    bool Enabled = true,
    bool SupportsToolCalling = true);
