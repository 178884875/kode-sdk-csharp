using KodaClaw.Contracts;
using KodaClaw.Runtime;
using KodaClaw.Workspace;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Configuration;

namespace KodaClaw.Gateway;

internal static class RuntimeConfigurationBootstrap
{
    public static RuntimeConfigurationSnapshot Resolve(IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(configuration);

        var secretStore = new PlatformSecretStore();
        var directResult = ResolveDirectConfiguration(configuration, secretStore);
        if (!string.IsNullOrWhiteSpace(directResult.DefaultModel))
        {
            return directResult;
        }

        var defaultEndpoint = TryLoadDefaultModelEndpoint(configuration);
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

    private static StoredModelEndpoint? TryLoadDefaultModelEndpoint(IConfiguration configuration)
    {
        var workspaceOptions = new KodaClawWorkspaceOptions
        {
            RootPath = configuration["KODACLAW_WORKSPACE_ROOT"]
                ?? configuration["Workspace:RootPath"],
        };

        var databasePath = Path.Combine(
            workspaceOptions.ResolveRootPath(),
            KodaClawWorkspaceLayout.ConfigDirectory,
            KodaClawWorkspaceLayout.ControlPlaneDatabaseFile);
        if (!File.Exists(databasePath))
        {
            return null;
        }

        var builder = new SqliteConnectionStringBuilder
        {
            DataSource = databasePath,
        };

        using var connection = new SqliteConnection(builder.ToString());
        connection.Open();

        var columns = LoadColumns(connection);
        if (columns.Count == 0 || !columns.Contains("is_default"))
        {
            return null;
        }

        using var command = connection.CreateCommand();
        command.CommandText =
            $"""
            SELECT
                provider,
                model_id,
                base_url,
                api_key_env,
                {GetSecretRefProjection(columns)},
                enabled
            FROM model_endpoints
            WHERE is_default = 1
            LIMIT 1;
            """;

        using var reader = command.ExecuteReader();
        if (!reader.Read())
        {
            return null;
        }

        return new StoredModelEndpoint(
            Provider: Enum.Parse<ModelProviderKind>(reader.GetString(0), ignoreCase: true),
            ModelId: reader.GetString(1),
            BaseUrl: reader.IsDBNull(2) ? null : reader.GetString(2),
            ApiKeyEnvironmentVariable: reader.IsDBNull(3) ? null : reader.GetString(3),
            ApiKeySecretRef: reader.IsDBNull(4) ? null : reader.GetString(4),
            Enabled: reader.GetInt64(5) != 0);
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

    private static HashSet<string> LoadColumns(SqliteConnection connection)
    {
        using var command = connection.CreateCommand();
        command.CommandText = "PRAGMA table_info(model_endpoints);";

        var columns = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        using var reader = command.ExecuteReader();
        while (reader.Read())
        {
            columns.Add(reader.GetString(1));
        }

        return columns;
    }

    private static string GetSecretRefProjection(HashSet<string> columns)
    {
        return columns.Contains("api_key_secret_ref")
            ? "api_key_secret_ref"
            : "NULL";
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
