using Kode.Agent.Sdk.Core.Abstractions;
using Kode.Agent.Sdk.Core.Types;
using Kode.Agent.Sdk.Infrastructure.Providers;
using KodaClaw.Contracts;
using KodaClaw.ModelHub;

namespace KodaClaw.Runtime;

/// <summary>
/// Registry-first IModelProvider: resolves the appropriate endpoint from
/// IModelRegistryRepository based on required capabilities inferred from the
/// request content, then constructs the matching LLM provider.
/// Falls back to DynamicModelProvider (env-var path) when the registry is empty.
/// </summary>
public sealed class RegistryAwareModelProvider : IModelProvider
{
    private const string DiagnosticSource = "registry_aware_model_provider";

    private readonly IModelRegistryRepository _registry;
    private readonly ISecretStore _secretStore;
    private readonly IRuntimeModelProviderFactory _factory;
    private readonly DynamicModelProvider _fallback;
    private readonly IDiagnosticsService? _diagnosticsService;

    // Cache providers by (endpointId, resolvedApiKey) so that each unique endpoint
    // reuses one HttpClient/connection-pool instead of creating a new one per LLM call.
    // This is critical for cloud deployments where socket exhaustion is a real risk.
    private readonly System.Collections.Concurrent.ConcurrentDictionary<string, IModelProvider> _providerCache = new();

    public RegistryAwareModelProvider(
        IModelRegistryRepository registry,
        ISecretStore secretStore,
        IRuntimeModelProviderFactory factory,
        DynamicModelProvider fallback,
        IDiagnosticsService? diagnosticsService = null)
    {
        _registry = registry ?? throw new ArgumentNullException(nameof(registry));
        _secretStore = secretStore ?? throw new ArgumentNullException(nameof(secretStore));
        _factory = factory ?? throw new ArgumentNullException(nameof(factory));
        _fallback = fallback ?? throw new ArgumentNullException(nameof(fallback));
        _diagnosticsService = diagnosticsService;
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
                ModelCapabilitySet.Text, cancellationToken);

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
        var required = InferRequiredCapabilities(request);
        var endpoint = await _registry.ResolveDefaultForAsync(required, cancellationToken);

        // Capability-degraded fallback: no multimodal endpoint found, try Text-only
        // and strip unsupported content blocks from the request.
        if (endpoint is null && required != ModelCapabilitySet.Text)
        {
            endpoint = await _registry.ResolveDefaultForAsync(
                ModelCapabilitySet.Text, cancellationToken);

            if (endpoint is not null)
            {
                var stripped = StripUnsupportedContent(request, endpoint.Capabilities);
                if (stripped != request)
                {
                    RecordDegradationEvent(endpoint, required, endpoint.Capabilities);
                    request = stripped;
                }
            }
        }

        if (endpoint is not null)
        {
            var provider = await BuildProviderAsync(endpoint, cancellationToken);
            var normalized = NormalizeRequest(request, endpoint);
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

        // Cache key: endpointId + apiKey (apiKey included so rotation takes effect immediately).
        var cacheKey = $"{endpoint.Id}:{apiKey}";
        if (_providerCache.TryGetValue(cacheKey, out var cached))
            return cached;

        var (providerKind, snapshot) = endpoint.Provider switch
        {
            ModelProviderKind.Anthropic or ModelProviderKind.AnthropicCompatible =>
                (RuntimeProviderKind.Anthropic, new RuntimeConfigurationSnapshot(
                    DefaultModel: endpoint.ModelId,
                    OpenAIApiKey: null,
                    OpenAIBaseUrl: null,
                    AnthropicApiKey: apiKey,
                    AnthropicBaseUrl: endpoint.BaseUrl,
                    CustomHeaders: endpoint.CustomHeaders)),

            ModelProviderKind.OpenAI or ModelProviderKind.OpenAICompatible =>
                (RuntimeProviderKind.OpenAI, new RuntimeConfigurationSnapshot(
                    DefaultModel: endpoint.ModelId,
                    OpenAIApiKey: apiKey,
                    OpenAIBaseUrl: endpoint.BaseUrl,
                    AnthropicApiKey: null,
                    AnthropicBaseUrl: null,
                    CustomHeaders: endpoint.CustomHeaders)),

            ModelProviderKind.OpenAIResponses =>
                (RuntimeProviderKind.OpenAIResponses, new RuntimeConfigurationSnapshot(
                    DefaultModel: endpoint.ModelId,
                    OpenAIApiKey: apiKey,
                    OpenAIBaseUrl: endpoint.BaseUrl,
                    AnthropicApiKey: null,
                    AnthropicBaseUrl: null,
                    CustomHeaders: endpoint.CustomHeaders)),

            _ => throw new InvalidOperationException(
                $"Unsupported provider kind '{endpoint.Provider}' for endpoint '{endpoint.Id}'."),
        };

        var provider = _factory.Create(providerKind, snapshot);
        // GetOrAdd is atomic: if two concurrent calls race, only one provider is kept.
        return _providerCache.GetOrAdd(cacheKey, provider);
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

    /// <summary>
    /// Normalizes the request for the resolved endpoint.
    /// Throws <see cref="InvalidOperationException"/> if the endpoint does not
    /// support tool calling but the request carries tool schemas — fail fast with
    /// a clear message rather than silently stripping tools.
    /// </summary>
    private static ModelRequest NormalizeRequest(ModelRequest request, ModelEndpoint endpoint)
    {
        var normalized = request;

        if (!string.IsNullOrWhiteSpace(endpoint.ModelId) &&
            !string.Equals(request.Model, endpoint.ModelId, StringComparison.Ordinal))
        {
            normalized = normalized with { Model = endpoint.ModelId };
        }

        if (endpoint.MaxOutputTokens > 0 && normalized.MaxTokens is null)
        {
            normalized = normalized with { MaxTokens = endpoint.MaxOutputTokens };
        }

        if (!endpoint.SupportsToolCalling && normalized.Tools is { Count: > 0 })
        {
            throw new InvalidOperationException(
                $"Model endpoint '{endpoint.DisplayName}' (id: {endpoint.Id}) does not support " +
                "tool calling. KodaClaw sessions require tool calling. " +
                "Please configure a model that supports function calling, or update the endpoint's " +
                "SupportsToolCalling setting if the model has been updated.");
        }

        return normalized;
    }

    /// <summary>
    /// Infers required ModelCapabilitySet from the content blocks present in the request messages.
    /// Text is always required. Additional capabilities are added for image/video/file blocks.
    /// </summary>
    private static ModelCapabilitySet InferRequiredCapabilities(ModelRequest request)
    {
        var required = ModelCapabilitySet.Text;
        foreach (var message in request.Messages)
        {
            foreach (var block in message.Content)
            {
                required |= block switch
                {
                    ImageContent => ModelCapabilitySet.Image,
                    VideoContent => ModelCapabilitySet.Video,
                    FileContent  => ModelCapabilitySet.File,
                    _            => ModelCapabilitySet.None,
                };
            }
        }
        return required;
    }

    /// <summary>
    /// Strips content blocks that require capabilities the endpoint does not have.
    /// Used when degrading from a multimodal request to a text-only endpoint.
    /// </summary>
    private static ModelRequest StripUnsupportedContent(
        ModelRequest request,
        ModelCapabilitySet supported)
    {
        var messages = request.Messages
            .Select(msg =>
            {
                var filtered = msg.Content
                    .Where(block => block switch
                    {
                        ImageContent => supported.HasFlag(ModelCapabilitySet.Image),
                        VideoContent => supported.HasFlag(ModelCapabilitySet.Video),
                        FileContent  => supported.HasFlag(ModelCapabilitySet.File),
                        _            => true,
                    })
                    .ToList();

                return (IReadOnlyList<ContentBlock>)filtered == msg.Content
                    ? msg
                    : msg with { Content = filtered };
            })
            .ToList();

        return request with { Messages = messages };
    }

    private void RecordDegradationEvent(
        ModelEndpoint endpoint,
        ModelCapabilitySet requested,
        ModelCapabilitySet available)
    {
        _diagnosticsService?.Record(new DiagnosticEvent(
            Id: Guid.NewGuid().ToString("N"),
            Source: DiagnosticSource,
            EventType: "multimodal_content_degraded",
            Level: "Warning",
            Message: $"No endpoint found for capabilities '{requested}'. " +
                     $"Degraded to endpoint '{endpoint.DisplayName}' with capabilities '{available}'. " +
                     "Unsupported content blocks (image/video/file) have been stripped from the request.",
            Timestamp: DateTimeOffset.UtcNow,
            Attributes: new Dictionary<string, string?>
            {
                ["endpointId"]          = endpoint.Id,
                ["endpointDisplayName"] = endpoint.DisplayName,
                ["requestedCapabilities"] = requested.ToString(),
                ["availableCapabilities"] = available.ToString(),
            }));
    }
}
