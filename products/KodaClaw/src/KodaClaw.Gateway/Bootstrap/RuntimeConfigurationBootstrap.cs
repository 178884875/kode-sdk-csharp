using System.Text.Json;
using System.Text.Json.Serialization;
using KodaClaw.Contracts;
using KodaClaw.Runtime;
using KodaClaw.Workspace;
using Microsoft.Extensions.Configuration;

namespace KodaClaw.Gateway;

internal static class RuntimeConfigurationBootstrap
{
    public static RuntimeConfigurationSnapshot Resolve(IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(configuration);

        var workspaceOptions = new KodaClawWorkspaceOptions
        {
            RootPath = configuration["KODACLAW_WORKSPACE_ROOT"] ?? configuration["Workspace:RootPath"],
        };
        var secretStore = PlatformSecretStore.CreateForCurrentPlatform(workspaceOptions);
        var directResult = ResolveDirectConfiguration(configuration, secretStore);
        if (!string.IsNullOrWhiteSpace(directResult.DefaultModel))
        {
            return directResult;
        }

        var defaultEndpoint = TryLoadDefaultModelEndpoint(workspaceOptions);
        if (defaultEndpoint is null || !defaultEndpoint.Enabled)
        {
            return directResult;
        }

        var resolvedApiKey = ResolveModelEndpointApiKey(defaultEndpoint, secretStore, configuration);
        return defaultEndpoint.Provider switch
        {
            ModelProviderKind.OpenAI or ModelProviderKind.OpenAICompatible => directResult with
            {
                DefaultModel = defaultEndpoint.ModelId,
                OpenAIApiKey = resolvedApiKey,
                OpenAIBaseUrl = NormalizeBaseUrl(defaultEndpoint.BaseUrl),
                AnthropicApiKey = null,
                AnthropicBaseUrl = null,
            },
            ModelProviderKind.Anthropic or ModelProviderKind.AnthropicCompatible => directResult with
            {
                DefaultModel = defaultEndpoint.ModelId,
                OpenAIApiKey = null,
                OpenAIBaseUrl = null,
                AnthropicApiKey = resolvedApiKey,
                AnthropicBaseUrl = NormalizeBaseUrl(defaultEndpoint.BaseUrl),
            },
            _ => directResult,
        };
    }

    private static RuntimeConfigurationSnapshot ResolveDirectConfiguration(
        IConfiguration configuration,
        ISecretStore secretStore)
    {
        return new RuntimeConfigurationSnapshot(
            DefaultModel: Normalize(configuration["KODACLAW_DEFAULT_MODEL"] ?? configuration["Runtime:DefaultModel"]),
            OpenAIApiKey: ResolveSecretOrValue(
                configuration,
                secretStore,
                secretRefKeys: ["OPENAI_API_KEY_SECRET_REF", "Runtime:OpenAIApiKeySecretRef"],
                valueKeys: ["OPENAI_API_KEY", "Runtime:OpenAIApiKey"]),
            OpenAIBaseUrl: NormalizeBaseUrl(configuration["Runtime:OpenAIBaseUrl"]),
            AnthropicApiKey: ResolveSecretOrValue(
                configuration,
                secretStore,
                secretRefKeys: ["ANTHROPIC_API_KEY_SECRET_REF", "Runtime:AnthropicApiKeySecretRef"],
                valueKeys: ["ANTHROPIC_API_KEY", "Runtime:AnthropicApiKey"]),
            AnthropicBaseUrl: NormalizeBaseUrl(configuration["Runtime:AnthropicBaseUrl"]));
    }

    private static string? ResolveSecretOrValue(
        IConfiguration configuration,
        ISecretStore secretStore,
        IReadOnlyList<string> secretRefKeys,
        IReadOnlyList<string> valueKeys)
    {
        foreach (var secretRefKey in secretRefKeys)
        {
            var secretRefValue = Normalize(configuration[secretRefKey]);
            if (!SecretRef.TryParse(secretRefValue, out var secretRef))
            {
                continue;
            }

            var resolved = secretStore.GetAsync(secretRef).GetAwaiter().GetResult();
            if (!string.IsNullOrWhiteSpace(resolved))
            {
                return resolved.Trim();
            }
        }

        foreach (var valueKey in valueKeys)
        {
            var configuredValue = Normalize(configuration[valueKey]);
            if (!string.IsNullOrWhiteSpace(configuredValue))
            {
                return configuredValue;
            }
        }

        return null;
    }

    private static StoredModelEndpoint? TryLoadDefaultModelEndpoint(KodaClawWorkspaceOptions workspaceOptions)
    {
        var modelsDir = Path.Combine(
            workspaceOptions.ResolveRootPath(),
            KodaClawWorkspaceLayout.ConfigDirectory,
            "models");

        if (!Directory.Exists(modelsDir))
        {
            return null;
        }

        var jsonOptions = new JsonSerializerOptions { PropertyNameCaseInsensitive = true };
        jsonOptions.Converters.Add(new JsonStringEnumConverter());

        foreach (var file in Directory.EnumerateFiles(modelsDir, "*.json"))
        {
            try
            {
                var json = File.ReadAllText(file);
                var endpoint = JsonSerializer.Deserialize<ModelEndpoint>(json, jsonOptions);
                if (endpoint is { IsDefault: true })
                {
                    return new StoredModelEndpoint(
                        Provider: endpoint.Provider,
                        ModelId: endpoint.ModelId,
                        BaseUrl: endpoint.BaseUrl,
                        ApiKeyEnvironmentVariable: endpoint.ApiKeyEnvironmentVariable,
                        ApiKeySecretRef: endpoint.ApiKeySecretRef,
                        Enabled: endpoint.Enabled);
                }
            }
            catch
            {
                // 忽略单文件解析失败，继续扫描其他文件
            }
        }

        return null;
    }

    private static string ResolveModelEndpointApiKey(
        StoredModelEndpoint endpoint,
        ISecretStore secretStore,
        IConfiguration configuration)
    {
        if (SecretRef.TryParse(endpoint.ApiKeySecretRef, out var secretRef))
        {
            var resolvedFromSecretStore = secretStore.GetAsync(secretRef).GetAwaiter().GetResult();
            if (!string.IsNullOrWhiteSpace(resolvedFromSecretStore))
            {
                return resolvedFromSecretStore.Trim();
            }
        }

        var environmentVariable = Normalize(endpoint.ApiKeyEnvironmentVariable);
        return string.IsNullOrWhiteSpace(environmentVariable)
            ? string.Empty
            : Normalize(Environment.GetEnvironmentVariable(environmentVariable) ?? configuration[environmentVariable]) ?? string.Empty;
    }

    private static string? Normalize(string? value)
    {
        if (value is null)
        {
            return null;
        }

        var normalized = value.Trim();
        return normalized.Length == 0 ? null : normalized;
    }

    private static string? NormalizeBaseUrl(string? value)
    {
        var normalized = Normalize(value);
        return normalized?.TrimEnd('/');
    }

    private sealed record StoredModelEndpoint(
        ModelProviderKind Provider,
        string ModelId,
        string? BaseUrl,
        string? ApiKeyEnvironmentVariable,
        string? ApiKeySecretRef,
        bool Enabled);
}
