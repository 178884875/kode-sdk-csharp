using System.Text;
using KodaClaw.Contracts;
using Kode.Agent.Sdk.Core.Abstractions;
using Kode.Agent.Sdk.Tools;

namespace KodaClaw.Runtime;

/// <summary>
/// Agent tool for managing model endpoint configuration at runtime.
/// Supports listing, adding, setting a default, and deleting model endpoints.
/// API keys are stored securely in the OS keychain via ISecretStore.
/// </summary>
public sealed class ConfigUpdateTool : ToolBase<ConfigUpdateArgs>
{
    private readonly IModelRegistryRepository _registry;
    private readonly ISecretStore _secretStore;
    private readonly IDiagnosticsService? _diagnosticsService;

    public ConfigUpdateTool(
        IModelRegistryRepository registry,
        ISecretStore secretStore,
        IDiagnosticsService? diagnosticsService = null)
    {
        _registry = registry ?? throw new ArgumentNullException(nameof(registry));
        _secretStore = secretStore ?? throw new ArgumentNullException(nameof(secretStore));
        _diagnosticsService = diagnosticsService;
    }

    public override string Name => "config_update";

    public override string Description =>
        "Manage KodaClaw model endpoint configuration. " +
        "Use action='list' to see all configured model endpoints. " +
        "Use action='add' to add a new model endpoint (provider, modelId, and apiKey required). " +
        "Use action='set_default' to change which endpoint is used by default (endpointId required). " +
        "Use action='delete' to remove an endpoint (endpointId required).\n\n" +
        "Provider values: 'Anthropic', 'AnthropicCompatible', 'OpenAI', 'OpenAICompatible'.\n" +
        "API keys are stored securely in the OS keychain and never appear in plain text on disk.";

    public override object InputSchema => JsonSchemaBuilder.BuildSchema<ConfigUpdateArgs>();

    public override ToolAttributes Attributes => new()
    {
        ReadOnly = false,
        RequiresApproval = false,
    };

    protected override async Task<ToolResult> ExecuteAsync(
        ConfigUpdateArgs args,
        ToolContext context,
        CancellationToken cancellationToken)
    {
        return args.Action?.ToLowerInvariant() switch
        {
            "list" => await ListAsync(cancellationToken),
            "add"  => await AddAsync(args, cancellationToken),
            "set_default" => await SetDefaultAsync(args, cancellationToken),
            "delete" => await DeleteAsync(args, cancellationToken),
            _ => ToolResult.Fail($"Unknown action '{args.Action}'. Valid values: list, add, set_default, delete."),
        };
    }

    // ── list ───────────────────────────────────────────────────────────────────

    private async Task<ToolResult> ListAsync(CancellationToken ct)
    {
        var endpoints = await _registry.ListAsync(ct);
        var result = endpoints.Select(e => new
        {
            id = e.Id,
            displayName = e.DisplayName,
            provider = e.Provider.ToString(),
            modelId = e.ModelId,
            baseUrl = e.BaseUrl,
            isDefault = e.IsDefault,
            enabled = e.Enabled,
            capabilities = e.Capabilities.ToString(),
            contextWindowSize = e.ContextWindowSize,
        }).ToList();

        return ToolResult.Ok(new { endpoints = result, count = result.Count });
    }

    // ── add ────────────────────────────────────────────────────────────────────

    private async Task<ToolResult> AddAsync(ConfigUpdateArgs args, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(args.ModelId))
            return ToolResult.Fail("'modelId' is required for action='add'.");
        if (string.IsNullOrWhiteSpace(args.Provider))
            return ToolResult.Fail("'provider' is required for action='add'.");
        if (string.IsNullOrWhiteSpace(args.ApiKey))
            return ToolResult.Fail("'apiKey' is required for action='add'.");

        if (!Enum.TryParse<ModelProviderKind>(args.Provider, ignoreCase: true, out var providerKind))
            return ToolResult.Fail($"Unknown provider '{args.Provider}'. Valid values: Anthropic, AnthropicCompatible, OpenAI, OpenAICompatible.");

        var displayName = !string.IsNullOrWhiteSpace(args.DisplayName)
            ? args.DisplayName.Trim()
            : $"{args.Provider} / {args.ModelId}";

        var id = Slugify($"{args.Provider}-{args.ModelId}");

        // Ensure uniqueness — append random suffix when id already exists.
        var existing = await _registry.GetByIdAsync(id, ct);
        if (existing is not null)
            id = $"{id}-{Guid.NewGuid():N}"[..Math.Min(64, id.Length + 9)];

        // Store the API key in the OS keychain.
        var secretRef = new SecretRef("keychain", "model-endpoint", id);
        await _secretStore.UpsertAsync(secretRef, args.ApiKey.Trim(), ct);

        var now = DateTimeOffset.UtcNow;
        var endpoint = new ModelEndpoint(
            Id: id,
            DisplayName: displayName,
            Provider: providerKind,
            ModelId: args.ModelId.Trim(),
            BaseUrl: string.IsNullOrWhiteSpace(args.BaseUrl) ? null : args.BaseUrl.Trim().TrimEnd('/'),
            ApiKeyEnvironmentVariable: null,
            ApiKeySecretRef: secretRef.ToReferenceString(),
            Enabled: true,
            Capabilities: ModelCapabilitySet.Text,
            IsDefault: false,
            CreatedAt: now,
            UpdatedAt: now,
            ContextWindowSize: args.ContextWindowSize ?? 128_000);

        await _registry.AddAsync(endpoint, ct);

        // If no default exists, set this one as default automatically.
        var all = await _registry.ListAsync(ct);
        if (!all.Any(m => m.IsDefault))
            await _registry.SetDefaultAsync(id, DateTimeOffset.UtcNow, ct);

        _diagnosticsService?.Record(new DiagnosticEvent(
            Id: Guid.NewGuid().ToString("N"),
            Source: "runtime.config_update",
            EventType: "model_endpoint.added",
            Level: "info",
            Message: $"Model endpoint '{displayName}' (id={id}) added by agent.",
            Timestamp: DateTimeOffset.UtcNow));

        return ToolResult.Ok(new
        {
            ok = true,
            id,
            displayName,
            provider = providerKind.ToString(),
            modelId = endpoint.ModelId,
        });
    }

    // ── set_default ────────────────────────────────────────────────────────────

    private async Task<ToolResult> SetDefaultAsync(ConfigUpdateArgs args, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(args.EndpointId))
            return ToolResult.Fail("'endpointId' is required for action='set_default'.");

        var success = await _registry.SetDefaultAsync(args.EndpointId.Trim(), DateTimeOffset.UtcNow, ct);
        if (!success)
            return ToolResult.Fail($"Endpoint '{args.EndpointId}' not found.");

        _diagnosticsService?.Record(new DiagnosticEvent(
            Id: Guid.NewGuid().ToString("N"),
            Source: "runtime.config_update",
            EventType: "model_endpoint.default_changed",
            Level: "info",
            Message: $"Default model endpoint changed to '{args.EndpointId}' by agent.",
            Timestamp: DateTimeOffset.UtcNow));

        return ToolResult.Ok(new { ok = true, defaultEndpointId = args.EndpointId.Trim() });
    }

    // ── delete ─────────────────────────────────────────────────────────────────

    private async Task<ToolResult> DeleteAsync(ConfigUpdateArgs args, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(args.EndpointId))
            return ToolResult.Fail("'endpointId' is required for action='delete'.");

        var id = args.EndpointId.Trim();
        var success = await _registry.DeleteAsync(id, ct);
        if (!success)
            return ToolResult.Fail($"Endpoint '{id}' not found.");

        // Best-effort: delete the associated keychain secret if it follows our naming convention.
        try
        {
            var secretRef = new SecretRef("keychain", "model-endpoint", id);
            await _secretStore.DeleteAsync(secretRef, ct);
        }
        catch
        {
            // Secret may not exist (e.g., endpoint was using an env-var key); ignore.
        }

        _diagnosticsService?.Record(new DiagnosticEvent(
            Id: Guid.NewGuid().ToString("N"),
            Source: "runtime.config_update",
            EventType: "model_endpoint.deleted",
            Level: "info",
            Message: $"Model endpoint '{id}' deleted by agent.",
            Timestamp: DateTimeOffset.UtcNow));

        return ToolResult.Ok(new { ok = true, deletedId = id });
    }

    // ── utilities ──────────────────────────────────────────────────────────────

    private static string Slugify(string input)
    {
        // lowercase, non-alphanumeric → '-', collapse consecutive '-', trim, max 64 chars
        var sb = new StringBuilder();
        var prevDash = true; // start true to strip leading dashes
        foreach (var c in input.ToLowerInvariant())
        {
            if (char.IsLetterOrDigit(c))
            {
                sb.Append(c);
                prevDash = false;
            }
            else if (!prevDash)
            {
                sb.Append('-');
                prevDash = true;
            }
        }

        var slug = sb.ToString().TrimEnd('-');
        return slug.Length > 64 ? slug[..64] : (slug.Length == 0 ? "endpoint" : slug);
    }
}

/// <summary>
/// Arguments for the config_update tool.
/// </summary>
public sealed class ConfigUpdateArgs
{
    [ToolParameter(Description = "Action to perform: 'list', 'add', 'set_default', or 'delete'.")]
    public required string Action { get; init; }

    [ToolParameter(Description = "Human-readable display name for the endpoint. Used for 'add'.", Required = false)]
    public string? DisplayName { get; init; }

    [ToolParameter(Description = "Provider kind: 'Anthropic', 'AnthropicCompatible', 'OpenAI', 'OpenAICompatible'. Required for 'add'.", Required = false)]
    public string? Provider { get; init; }

    [ToolParameter(Description = "Model identifier (e.g. 'claude-sonnet-4-20250514', 'gpt-4o'). Required for 'add'.", Required = false)]
    public string? ModelId { get; init; }

    [ToolParameter(Description = "API key value. Stored securely in the OS keychain. Required for 'add'.", Required = false)]
    public string? ApiKey { get; init; }

    [ToolParameter(Description = "Base URL for compatible providers (e.g. 'https://my-proxy.com/v1'). Omit for official Anthropic/OpenAI endpoints.", Required = false)]
    public string? BaseUrl { get; init; }

    [ToolParameter(Description = "Endpoint ID. Required for 'set_default' and 'delete'.", Required = false)]
    public string? EndpointId { get; init; }

    [ToolParameter(Description = "Context window size in tokens. Defaults to 128000.", Required = false)]
    public int? ContextWindowSize { get; init; }
}
