namespace KodaClaw.Contracts;

public sealed record CreateModelEndpointRequest(
    string DisplayName,
    ModelProviderKind Provider,
    string ModelId,
    string? BaseUrl = null,
    string? ApiKeyEnvironmentVariable = null,
    string? ApiKeySecretRef = null,
    bool Enabled = true,
    ModelCapabilitySet Capabilities = ModelCapabilitySet.Text,
    int ContextWindowSize = 128_000,
    int MaxOutputTokens = 8192,
    bool IsReasoning = false,
    string? ApiKeyValue = null,
    IReadOnlyDictionary<string, string>? CustomHeaders = null);
