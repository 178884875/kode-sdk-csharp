using System.Globalization;
using KodaClaw.Contracts;
using KodaClaw.Workspace;
using Microsoft.Data.Sqlite;

namespace KodaClaw.ModelHub;

public sealed class SqliteModelRegistryRepository : IModelRegistryRepository
{
    private const string TableName = "model_endpoints";

    private readonly SemaphoreSlim _initializationLock = new(1, 1);
    private readonly IWorkspaceService _workspaceService;
    private string? _databasePath;
    private bool _initialized;

    public SqliteModelRegistryRepository(IWorkspaceService workspaceService)
    {
        _workspaceService = workspaceService ?? throw new ArgumentNullException(nameof(workspaceService));
    }

    public async Task AddAsync(ModelEndpoint endpoint, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(endpoint);
        ValidateEndpoint(endpoint);

        await using var connection = await OpenConnectionAsync(cancellationToken);
        await using var transaction = (SqliteTransaction)await connection.BeginTransactionAsync(cancellationToken);

        if (endpoint.IsDefault)
        {
            await ClearDefaultAsync(connection, transaction, cancellationToken);
        }

        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText =
            $"""
            INSERT INTO {TableName} (
                id,
                display_name,
                provider,
                model_id,
                base_url,
                api_key_env,
                api_key_secret_ref,
                enabled,
                supports_tool_calling,
                is_default,
                created_at,
                updated_at
            )
            VALUES (
                $id,
                $displayName,
                $provider,
                $modelId,
                $baseUrl,
                $apiKey,
                $apiKeySecretRef,
                $enabled,
                $supportsToolCalling,
                $isDefault,
                $createdAt,
                $updatedAt
            );
            """;
        BindParameters(command, endpoint);
        await command.ExecuteNonQueryAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
    }

    public async Task<IReadOnlyList<ModelEndpoint>> ListAsync(CancellationToken cancellationToken = default)
    {
        await using var connection = await OpenConnectionAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText =
            $"""
            SELECT
                id,
                display_name,
                provider,
                model_id,
                base_url,
                api_key_env,
                api_key_secret_ref,
                enabled,
                supports_tool_calling,
                is_default,
                created_at,
                updated_at
            FROM {TableName}
            ORDER BY is_default DESC, created_at DESC;
            """;

        var list = new List<ModelEndpoint>();
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            list.Add(MapEndpoint(reader));
        }

        return list;
    }

    public async Task<ModelEndpoint?> GetByIdAsync(string id, CancellationToken cancellationToken = default)
    {
        ValidateId(id);

        await using var connection = await OpenConnectionAsync(cancellationToken);
        return await QuerySingleAsync(connection, id, cancellationToken);
    }

    public async Task<bool> UpdateAsync(ModelEndpoint endpoint, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(endpoint);
        ValidateEndpoint(endpoint);

        await using var connection = await OpenConnectionAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText =
            $"""
            UPDATE {TableName}
            SET
                display_name = $displayName,
                provider = $provider,
                model_id = $modelId,
                base_url = $baseUrl,
                api_key_env = $apiKey,
                api_key_secret_ref = $apiKeySecretRef,
                enabled = $enabled,
                supports_tool_calling = $supportsToolCalling,
                updated_at = $updatedAt
            WHERE id = $id;
            """;
        BindParameters(command, endpoint);

        var rows = await command.ExecuteNonQueryAsync(cancellationToken);
        return rows > 0;
    }

    public async Task<bool> DeleteAsync(string id, CancellationToken cancellationToken = default)
    {
        ValidateId(id);

        await using var connection = await OpenConnectionAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText =
            $"""
            DELETE FROM {TableName}
            WHERE id = $id;
            """;
        command.Parameters.AddWithValue("$id", id);

        var rows = await command.ExecuteNonQueryAsync(cancellationToken);
        return rows > 0;
    }

    public async Task<bool> SetDefaultAsync(string id, DateTimeOffset updatedAt, CancellationToken cancellationToken = default)
    {
        ValidateId(id);

        await using var connection = await OpenConnectionAsync(cancellationToken);
        await using var transaction = (SqliteTransaction)await connection.BeginTransactionAsync(cancellationToken);

        if (!await ExistsAsync(connection, transaction, id, cancellationToken))
        {
            return false;
        }

        await ClearDefaultAsync(connection, transaction, cancellationToken);

        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText =
            $"""
            UPDATE {TableName}
            SET is_default = 1,
                updated_at = $updatedAt
            WHERE id = $id;
            """;
        command.Parameters.AddWithValue("$id", id);
        command.Parameters.AddWithValue("$updatedAt", FormatTimestamp(updatedAt));

        var rows = await command.ExecuteNonQueryAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return rows > 0;
    }

    private async Task<string> EnsureDatabaseAsync(CancellationToken cancellationToken)
    {
        if (_initialized && _databasePath is not null)
        {
            return _databasePath;
        }

        await _initializationLock.WaitAsync(cancellationToken);
        try
        {
            if (_initialized && _databasePath is not null)
            {
                return _databasePath;
            }

            var snapshot = await _workspaceService.EnsureInitializedAsync(cancellationToken);
            var databasePath = Path.Combine(
                snapshot.RootPath,
                KodaClawWorkspaceLayout.ConfigDirectory,
                KodaClawWorkspaceLayout.ControlPlaneDatabaseFile);
            Directory.CreateDirectory(Path.GetDirectoryName(databasePath)!);

            var builder = new SqliteConnectionStringBuilder
            {
                DataSource = databasePath,
            };

            await using var connection = new SqliteConnection(builder.ToString());
            await connection.OpenAsync(cancellationToken);

            await using var command = connection.CreateCommand();
            command.CommandText =
                $"""
                CREATE TABLE IF NOT EXISTS {TableName} (
                    id TEXT PRIMARY KEY,
                    display_name TEXT NOT NULL,
                    provider TEXT NOT NULL,
                    model_id TEXT NOT NULL,
                    base_url TEXT,
                    api_key_env TEXT,
                    api_key_secret_ref TEXT,
                    enabled INTEGER NOT NULL,
                    supports_tool_calling INTEGER NOT NULL,
                    is_default INTEGER NOT NULL,
                    created_at TEXT NOT NULL,
                    updated_at TEXT NOT NULL
                );
                CREATE UNIQUE INDEX IF NOT EXISTS ix_{TableName}_is_default
                    ON {TableName}(is_default)
                    WHERE is_default = 1;
                """;
            await command.ExecuteNonQueryAsync(cancellationToken);
            await EnsureColumnExistsAsync(
                connection,
                columnName: "api_key_secret_ref",
                columnDefinition: "TEXT",
                cancellationToken);

            _databasePath = databasePath;
            _initialized = true;
            return databasePath;
        }
        finally
        {
            _initializationLock.Release();
        }
    }

    private async Task<SqliteConnection> OpenConnectionAsync(CancellationToken cancellationToken)
    {
        var databasePath = await EnsureDatabaseAsync(cancellationToken);
        var builder = new SqliteConnectionStringBuilder
        {
            DataSource = databasePath,
        };

        var connection = new SqliteConnection(builder.ToString());
        await connection.OpenAsync(cancellationToken);
        return connection;
    }

    private static async Task<bool> ExistsAsync(
        SqliteConnection connection,
        SqliteTransaction transaction,
        string id,
        CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText =
            $"""
            SELECT 1
            FROM {TableName}
            WHERE id = $id
            LIMIT 1;
            """;
        command.Parameters.AddWithValue("$id", id);

        var result = await command.ExecuteScalarAsync(cancellationToken);
        return result is not null;
    }

    private static async Task ClearDefaultAsync(
        SqliteConnection connection,
        SqliteTransaction transaction,
        CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText =
            $"""
            UPDATE {TableName}
            SET is_default = 0
            WHERE is_default = 1;
            """;
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    private async Task<ModelEndpoint?> QuerySingleAsync(
        SqliteConnection connection,
        string id,
        CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.CommandText =
            $"""
            SELECT
                id,
                display_name,
                provider,
                model_id,
                base_url,
                api_key_env,
                api_key_secret_ref,
                enabled,
                supports_tool_calling,
                is_default,
                created_at,
                updated_at
            FROM {TableName}
            WHERE id = $id
            LIMIT 1;
            """;
        command.Parameters.AddWithValue("$id", id);

        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        if (await reader.ReadAsync(cancellationToken))
        {
            return MapEndpoint(reader);
        }

        return null;
    }

    private static void BindParameters(SqliteCommand command, ModelEndpoint endpoint)
    {
        command.Parameters.AddWithValue("$id", endpoint.Id);
        command.Parameters.AddWithValue("$displayName", endpoint.DisplayName);
        command.Parameters.AddWithValue("$provider", endpoint.Provider.ToString());
        command.Parameters.AddWithValue("$modelId", endpoint.ModelId);
        command.Parameters.AddWithValue("$baseUrl", (object?)endpoint.BaseUrl ?? DBNull.Value);
        command.Parameters.AddWithValue("$apiKey", (object?)endpoint.ApiKeyEnvironmentVariable ?? DBNull.Value);
        command.Parameters.AddWithValue("$apiKeySecretRef", (object?)endpoint.ApiKeySecretRef ?? DBNull.Value);
        command.Parameters.AddWithValue("$enabled", endpoint.Enabled ? 1 : 0);
        command.Parameters.AddWithValue("$supportsToolCalling", endpoint.SupportsToolCalling ? 1 : 0);
        command.Parameters.AddWithValue("$isDefault", endpoint.IsDefault ? 1 : 0);
        command.Parameters.AddWithValue("$createdAt", FormatTimestamp(endpoint.CreatedAt));
        command.Parameters.AddWithValue("$updatedAt", FormatTimestamp(endpoint.UpdatedAt));
    }

    private static ModelEndpoint MapEndpoint(SqliteDataReader reader)
    {
        return new ModelEndpoint(
            Id: reader.GetString(0),
            DisplayName: reader.GetString(1),
            Provider: Enum.Parse<ModelProviderKind>(reader.GetString(2), ignoreCase: true),
            ModelId: reader.GetString(3),
            BaseUrl: reader.IsDBNull(4) ? null : reader.GetString(4),
            ApiKeyEnvironmentVariable: reader.IsDBNull(5) ? null : reader.GetString(5),
            ApiKeySecretRef: reader.IsDBNull(6) ? null : reader.GetString(6),
            Enabled: reader.GetInt64(7) != 0,
            SupportsToolCalling: reader.GetInt64(8) != 0,
            IsDefault: reader.GetInt64(9) != 0,
            CreatedAt: ParseTimestamp(reader.GetString(10)),
            UpdatedAt: ParseTimestamp(reader.GetString(11)));
    }

    private static void ValidateEndpoint(ModelEndpoint endpoint)
    {
        if (string.IsNullOrWhiteSpace(endpoint.Id))
        {
            throw new ArgumentException("Endpoint id is required.", nameof(endpoint.Id));
        }

        if (string.IsNullOrWhiteSpace(endpoint.DisplayName))
        {
            throw new ArgumentException("Display name is required.", nameof(endpoint.DisplayName));
        }

        if (string.IsNullOrWhiteSpace(endpoint.ModelId))
        {
            throw new ArgumentException("Model id is required.", nameof(endpoint.ModelId));
        }

        if (string.IsNullOrWhiteSpace(endpoint.BaseUrl))
        {
            if (endpoint.Provider is ModelProviderKind.OpenAICompatible or ModelProviderKind.AnthropicCompatible)
            {
                throw new ArgumentException("Compatible providers require a BaseUrl.", nameof(endpoint.BaseUrl));
            }
        }
        else if (!Uri.TryCreate(endpoint.BaseUrl, UriKind.Absolute, out var uri) ||
                 (uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps))
        {
            throw new ArgumentException(
                "When BaseUrl is provided it must be an absolute http or https URL.",
                nameof(endpoint.BaseUrl));
        }

        if (!string.IsNullOrWhiteSpace(endpoint.ApiKeyEnvironmentVariable) &&
            endpoint.ApiKeyEnvironmentVariable.Any(char.IsWhiteSpace))
        {
            throw new ArgumentException(
                "API key environment variable may not contain whitespace.",
                nameof(endpoint.ApiKeyEnvironmentVariable));
        }

        if (!string.IsNullOrWhiteSpace(endpoint.ApiKeySecretRef) &&
            !SecretRef.TryParse(endpoint.ApiKeySecretRef, out _))
        {
            throw new ArgumentException(
                "API key secret ref must use the '<provider>:<scope>:<key>' format.",
                nameof(endpoint.ApiKeySecretRef));
        }
    }

    private static async Task EnsureColumnExistsAsync(
        SqliteConnection connection,
        string columnName,
        string columnDefinition,
        CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.CommandText = $"PRAGMA table_info({TableName});";

        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            if (string.Equals(reader.GetString(1), columnName, StringComparison.OrdinalIgnoreCase))
            {
                return;
            }
        }

        await using var alterCommand = connection.CreateCommand();
        alterCommand.CommandText = $"ALTER TABLE {TableName} ADD COLUMN {columnName} {columnDefinition};";
        await alterCommand.ExecuteNonQueryAsync(cancellationToken);
    }

    private static string FormatTimestamp(DateTimeOffset value)
    {
        return value.ToString("O", CultureInfo.InvariantCulture);
    }

    private static DateTimeOffset ParseTimestamp(string rawValue)
    {
        return DateTimeOffset.Parse(rawValue, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind);
    }

    private static void ValidateId(string? id)
    {
        if (string.IsNullOrWhiteSpace(id))
        {
            throw new ArgumentException("Id is required.", nameof(id));
        }
    }
}
