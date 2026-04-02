using KodaClaw.Contracts;
using KodaClaw.ModelHub;
using KodaClaw.Runtime;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace KodaClaw.Gateway;

/// <summary>
/// Runs once at startup: seeds the model registry from env-var configuration
/// when the registry is empty. This ensures a smooth upgrade path for users
/// who previously configured the agent via environment variables.
///
/// After seeding, the Registry becomes the single source of truth and env vars
/// are no longer consulted for model routing (see RegistryAwareModelProvider).
/// </summary>
internal sealed class ModelRegistrySeedService : IHostedService
{
    private readonly IModelRegistryRepository _registry;
    private readonly IRuntimeConfigurationResolver _runtimeConfig;
    private readonly ILogger<ModelRegistrySeedService>? _logger;

    public ModelRegistrySeedService(
        IModelRegistryRepository registry,
        IRuntimeConfigurationResolver runtimeConfig,
        ILogger<ModelRegistrySeedService>? logger = null)
    {
        _registry = registry ?? throw new ArgumentNullException(nameof(registry));
        _runtimeConfig = runtimeConfig ?? throw new ArgumentNullException(nameof(runtimeConfig));
        _logger = logger;
    }

    public async Task StartAsync(CancellationToken cancellationToken)
    {
        var existing = await _registry.ListAsync(cancellationToken);
        if (existing.Count > 0)
        {
            // Registry already populated — nothing to seed.
            _logger?.LogDebug("ModelRegistrySeedService: registry has {Count} endpoints, skipping seed.", existing.Count);
            return;
        }

        var snapshot = _runtimeConfig.Resolve();
        var endpoint = TryBuildEndpoint(snapshot);
        if (endpoint is null)
        {
            _logger?.LogDebug("ModelRegistrySeedService: no env-var config found, nothing to seed.");
            return;
        }

        await _registry.AddAsync(endpoint, cancellationToken);
        await _registry.SetDefaultAsync(endpoint.Id, DateTimeOffset.UtcNow, cancellationToken);

        _logger?.LogInformation(
            "ModelRegistrySeedService: seeded endpoint '{DisplayName}' (provider={Provider}, modelId={ModelId}) from env-var config.",
            endpoint.DisplayName, endpoint.Provider, endpoint.ModelId);
    }

    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;

    private static ModelEndpoint? TryBuildEndpoint(RuntimeConfigurationSnapshot snapshot)
    {
        if (string.IsNullOrWhiteSpace(snapshot.DefaultModel))
            return null;

        ModelProviderKind provider;
        string? apiKeyEnvVar;

        var hasAnthropic = !string.IsNullOrWhiteSpace(snapshot.AnthropicApiKey);
        var hasOpenAi = !string.IsNullOrWhiteSpace(snapshot.OpenAIApiKey);
        var model = snapshot.DefaultModel;

        if (hasAnthropic && !hasOpenAi)
        {
            provider = ModelProviderKind.Anthropic;
            apiKeyEnvVar = "ANTHROPIC_API_KEY";
        }
        else if (hasOpenAi && !hasAnthropic)
        {
            provider = ModelProviderKind.OpenAI;
            apiKeyEnvVar = "OPENAI_API_KEY";
        }
        else if (hasAnthropic && hasOpenAi)
        {
            // Both set — pick by model name heuristic
            if (model.StartsWith("claude", StringComparison.OrdinalIgnoreCase))
            {
                provider = ModelProviderKind.Anthropic;
                apiKeyEnvVar = "ANTHROPIC_API_KEY";
            }
            else
            {
                provider = ModelProviderKind.OpenAI;
                apiKeyEnvVar = "OPENAI_API_KEY";
            }
        }
        else
        {
            return null;
        }

        var now = DateTimeOffset.UtcNow;
        var id = $"model-seed-{Guid.NewGuid():N}";

        return new ModelEndpoint(
            Id: id,
            DisplayName: $"{model} (auto-seeded)",
            Provider: provider,
            ModelId: model,
            BaseUrl: provider == ModelProviderKind.Anthropic
                ? snapshot.AnthropicBaseUrl
                : snapshot.OpenAIBaseUrl,
            ApiKeyEnvironmentVariable: apiKeyEnvVar,
            ApiKeySecretRef: null,
            Enabled: true,
            Capabilities: ModelCapabilitySet.Text,
            IsDefault: false,   // SetDefaultAsync called separately
            CreatedAt: now,
            UpdatedAt: now,
            ContextWindowSize: 128_000);
    }
}
