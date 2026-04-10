using System.Text;
using KodaClaw.Contracts;
using Microsoft.Extensions.Logging;

namespace KodaClaw.Gateway;

/// <summary>
/// Shared writer used by both the ENV-var bootstrap path (ConfigBootstrapService)
/// and the Setup Wizard path (POST /setup/complete).
///
/// Writes a default model endpoint into the registry if and only if the registry
/// is currently empty. Keys are stored in the OS Keychain via ISecretStore rather
/// than on disk in plaintext.
/// </summary>
internal sealed class ConfigBootstrapWriter
{
    private const string AnthropicDefaultId = "anthropic-default";
    private const string OpenAIDefaultId = "openai-default";

    private readonly IModelRegistryRepository _registry;
    private readonly ISecretStore _secretStore;
    private readonly ILogger<ConfigBootstrapWriter>? _logger;

    public ConfigBootstrapWriter(
        IModelRegistryRepository registry,
        ISecretStore secretStore,
        ILogger<ConfigBootstrapWriter>? logger = null)
    {
        _registry = registry ?? throw new ArgumentNullException(nameof(registry));
        _secretStore = secretStore ?? throw new ArgumentNullException(nameof(secretStore));
        _logger = logger;
    }

    // ── ENV-var bootstrap path ─────────────────────────────────────────────────

    /// <summary>
    /// If the model registry is empty, writes a default endpoint for the first
    /// non-null API key provided and marks it as the default.
    /// Anthropic key takes precedence over OpenAI when both are supplied.
    /// No-op if the registry already contains at least one endpoint.
    /// Used by <see cref="ConfigBootstrapService"/> for ENV-var seeding.
    /// </summary>
    public async Task WriteIfAbsentAsync(
        string? anthropicKey,
        string? openaiKey,
        CancellationToken cancellationToken = default)
    {
        var existing = await _registry.ListAsync(cancellationToken);
        if (existing.Count > 0)
        {
            _logger?.LogDebug(
                "ConfigBootstrapWriter: registry has {Count} endpoint(s), skipping.", existing.Count);
            return;
        }

        ModelEndpoint? endpoint = null;

        if (!string.IsNullOrWhiteSpace(anthropicKey))
        {
            var secretRef = new SecretRef("keychain", "config-bootstrap", "anthropic");
            await _secretStore.UpsertAsync(secretRef, anthropicKey.Trim(), cancellationToken);
            endpoint = BuildEndpoint(
                id: AnthropicDefaultId,
                displayName: "Claude Sonnet (default)",
                provider: ModelProviderKind.Anthropic,
                modelId: "claude-sonnet-4-20250514",
                secretRef: secretRef.ToReferenceString());

            _logger?.LogDebug("ConfigBootstrapWriter: stored Anthropic key in secret store.");
        }
        else if (!string.IsNullOrWhiteSpace(openaiKey))
        {
            var secretRef = new SecretRef("keychain", "config-bootstrap", "openai");
            await _secretStore.UpsertAsync(secretRef, openaiKey.Trim(), cancellationToken);
            endpoint = BuildEndpoint(
                id: OpenAIDefaultId,
                displayName: "GPT-4o (default)",
                provider: ModelProviderKind.OpenAI,
                modelId: "gpt-4o",
                secretRef: secretRef.ToReferenceString());

            _logger?.LogDebug("ConfigBootstrapWriter: stored OpenAI key in secret store.");
        }

        if (endpoint is null)
        {
            _logger?.LogDebug("ConfigBootstrapWriter: no API key provided, nothing to write.");
            return;
        }

        await _registry.AddAsync(endpoint, cancellationToken);
        await _registry.SetDefaultAsync(endpoint.Id, DateTimeOffset.UtcNow, cancellationToken);

        _logger?.LogInformation(
            "ConfigBootstrapWriter: wrote endpoint '{DisplayName}' " +
            "(provider={Provider}, modelId={ModelId}) and set as default.",
            endpoint.DisplayName, endpoint.Provider, endpoint.ModelId);
    }

    // ── Setup Wizard path ──────────────────────────────────────────────────────

    /// <summary>
    /// If the model registry is empty, writes a single endpoint for the specified
    /// provider / model combination and marks it as the default.
    /// No-op if the registry already contains at least one endpoint.
    /// Used by the Setup Wizard POST /setup/complete path.
    /// </summary>
    public async Task WriteIfAbsentAsync(
        ModelProviderKind provider,
        string modelId,
        string? apiKey,
        string? baseUrl,
        string? displayName,
        CancellationToken cancellationToken = default)
    {
        var existing = await _registry.ListAsync(cancellationToken);
        if (existing.Count > 0)
        {
            _logger?.LogDebug(
                "ConfigBootstrapWriter: registry has {Count} endpoint(s), skipping.", existing.Count);
            return;
        }

        var id = Slugify($"{provider}-{modelId}");
        var name = !string.IsNullOrWhiteSpace(displayName)
            ? displayName.Trim()
            : $"{provider} / {modelId}";

        string? secretRef = null;
        if (!string.IsNullOrWhiteSpace(apiKey))
        {
            var secret = new SecretRef("keychain", "config-bootstrap", id);
            await _secretStore.UpsertAsync(secret, apiKey.Trim(), cancellationToken);
            secretRef = secret.ToReferenceString();
            _logger?.LogDebug("ConfigBootstrapWriter: stored API key for '{Id}' in secret store.", id);
        }

        var endpoint = BuildEndpoint(
            id: id,
            displayName: name,
            provider: provider,
            modelId: modelId,
            secretRef: secretRef,
            baseUrl: string.IsNullOrWhiteSpace(baseUrl) ? null : baseUrl.Trim());

        await _registry.AddAsync(endpoint, cancellationToken);
        await _registry.SetDefaultAsync(id, DateTimeOffset.UtcNow, cancellationToken);

        _logger?.LogInformation(
            "ConfigBootstrapWriter: wrote endpoint '{DisplayName}' " +
            "(provider={Provider}, modelId={ModelId}) and set as default.",
            name, provider, modelId);
    }

    // ── helpers ────────────────────────────────────────────────────────────────

    private static ModelEndpoint BuildEndpoint(
        string id,
        string displayName,
        ModelProviderKind provider,
        string modelId,
        string? secretRef,
        string? baseUrl = null)
    {
        var now = DateTimeOffset.UtcNow;
        return new ModelEndpoint(
            Id: id,
            DisplayName: displayName,
            Provider: provider,
            ModelId: modelId,
            BaseUrl: baseUrl,
            ApiKeyEnvironmentVariable: null,
            ApiKeySecretRef: secretRef,
            Enabled: true,
            Capabilities: ModelCapabilitySet.Text,
            IsDefault: false,   // SetDefaultAsync is called right after AddAsync
            CreatedAt: now,
            UpdatedAt: now);
    }

    private static string Slugify(string input)
    {
        var sb = new StringBuilder();
        var prevDash = true;
        foreach (var c in input.ToLowerInvariant())
        {
            if (char.IsLetterOrDigit(c)) { sb.Append(c); prevDash = false; }
            else if (!prevDash) { sb.Append('-'); prevDash = true; }
        }
        var slug = sb.ToString().TrimEnd('-');
        return slug.Length == 0 ? "endpoint" : (slug.Length > 64 ? slug[..64] : slug);
    }
}
