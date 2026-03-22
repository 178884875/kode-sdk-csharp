using Kode.Agent.Sdk.Core.Abstractions;
using Kode.Agent.Sdk.Infrastructure.Providers;
using KodaClaw.Contracts;
using KodaClaw.ModelHub;

namespace KodaClaw.Runtime;

/// <summary>
/// Registry-first IModelProvider: resolves the default TextChat|ToolCalling endpoint
/// from IModelRegistryRepository and constructs the appropriate LLM provider.
/// Falls back to DynamicModelProvider (env-var path) when the registry is empty.
/// </summary>
public sealed class RegistryAwareModelProvider : IModelProvider
{
    private readonly IModelRegistryRepository _registry;
    private readonly ISecretStore _secretStore;
    private readonly IRuntimeModelProviderFactory _factory;
    private readonly DynamicModelProvider _fallback;

    public RegistryAwareModelProvider(
        IModelRegistryRepository registry,
        ISecretStore secretStore,
        IRuntimeModelProviderFactory factory,
        DynamicModelProvider fallback)
    {
        _registry = registry ?? throw new ArgumentNullException(nameof(registry));
        _secretStore = secretStore ?? throw new ArgumentNullException(nameof(secretStore));
        _factory = factory ?? throw new ArgumentNullException(nameof(factory));
        _fallback = fallback ?? throw new ArgumentNullException(nameof(fallback));
    }

    public string ProviderName => "registry";

    public async IAsyncEnumerable<StreamChunk> StreamAsync(
        ModelRequest request,
        [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        var (provider, normalizedRequest) = await ResolveAsync(request, cancellationToken);
        await foreach (var chunk in provider.StreamAsync(normalizedRequest, cancellationToken))
            yield return chunk;
    }

    public async Task<ModelResponse> CompleteAsync(
        ModelRequest request,
        CancellationToken cancellationToken = default)
    {
        var (provider, normalizedRequest) = await ResolveAsync(request, cancellationToken);
        return await provider.CompleteAsync(normalizedRequest, cancellationToken);
    }

    public async Task<bool> ValidateAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            var endpoint = await _registry.ResolveDefaultForAsync(
                ModelCapabilitySet.TextChat | ModelCapabilitySet.ToolCalling, cancellationToken);

            if (endpoint is not null)
            {
                var provider = await BuildProviderAsync(endpoint, cancellationToken);
                return await provider.ValidateAsync(cancellationToken);
            }
        }
        catch (InvalidOperationException)
        {
            // registry path not ready — fall through to env-var fallback
        }

        return await _fallback.ValidateAsync(cancellationToken);
    }

    // ── internals ─────────────────────────────────────────────────────────────

    private async Task<(IModelProvider Provider, ModelRequest Request)> ResolveAsync(
        ModelRequest request,
        CancellationToken cancellationToken)
    {
        var endpoint = await _registry.ResolveDefaultForAsync(
            ModelCapabilitySet.TextChat | ModelCapabilitySet.ToolCalling, cancellationToken);

        if (endpoint is not null)
        {
            var provider = await BuildProviderAsync(endpoint, cancellationToken);
            var normalized = NormalizeRequest(request, endpoint.ModelId);
            return (provider, normalized);
        }

        // No registry entry — delegate to env-var DynamicModelProvider
        return (_fallback, request);
    }

    private async Task<IModelProvider> BuildProviderAsync(
        ModelEndpoint endpoint,
        CancellationToken cancellationToken)
    {
        var apiKey = await ResolveApiKeyAsync(endpoint, cancellationToken);

        var (providerKind, snapshot) = endpoint.Provider switch
        {
            ModelProviderKind.Anthropic or ModelProviderKind.AnthropicCompatible =>
                (RuntimeProviderKind.Anthropic, new RuntimeConfigurationSnapshot(
                    DefaultModel: endpoint.ModelId,
                    OpenAIApiKey: null,
                    OpenAIBaseUrl: null,
                    AnthropicApiKey: apiKey,
                    AnthropicBaseUrl: endpoint.BaseUrl)),

            ModelProviderKind.OpenAI or ModelProviderKind.OpenAICompatible =>
                (RuntimeProviderKind.OpenAI, new RuntimeConfigurationSnapshot(
                    DefaultModel: endpoint.ModelId,
                    OpenAIApiKey: apiKey,
                    OpenAIBaseUrl: endpoint.BaseUrl,
                    AnthropicApiKey: null,
                    AnthropicBaseUrl: null)),

            _ => throw new InvalidOperationException(
                $"Unsupported provider kind '{endpoint.Provider}' for endpoint '{endpoint.Id}'."),
        };

        return _factory.Create(providerKind, snapshot);
    }

    private async Task<string> ResolveApiKeyAsync(
        ModelEndpoint endpoint,
        CancellationToken cancellationToken)
    {
        if (!string.IsNullOrWhiteSpace(endpoint.ApiKeySecretRef) &&
            SecretRef.TryParse(endpoint.ApiKeySecretRef, out var secretRef))
        {
            var secret = await _secretStore.GetAsync(secretRef, cancellationToken);
            if (!string.IsNullOrWhiteSpace(secret))
                return secret;
        }

        if (!string.IsNullOrWhiteSpace(endpoint.ApiKeyEnvironmentVariable))
        {
            var envVal = Environment.GetEnvironmentVariable(endpoint.ApiKeyEnvironmentVariable);
            if (!string.IsNullOrWhiteSpace(envVal))
                return envVal;
        }

        throw new InvalidOperationException(
            $"No API key configured for model endpoint '{endpoint.Id}'. " +
            "Configure a key in Models settings or set the corresponding environment variable.");
    }

    private static ModelRequest NormalizeRequest(ModelRequest request, string modelId)
    {
        if (string.IsNullOrWhiteSpace(modelId) ||
            string.Equals(request.Model, modelId, StringComparison.Ordinal))
        {
            return request;
        }

        return request with { Model = modelId };
    }
}
