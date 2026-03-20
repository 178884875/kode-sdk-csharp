using System.Globalization;
using System.Text;
using System.Text.Json;
using KodaClaw.Contracts;
using Microsoft.Data.Sqlite;

namespace KodaClaw.PluginHost.Registry;

public sealed class SqlitePluginRegistryRepository : IPluginRegistryRepository
{
    private const int DefaultLimit = 50;
    private const int MaxLimit = 200;
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    private readonly SqlitePluginRegistryDatabase _database;

    public SqlitePluginRegistryRepository(IWorkspaceService workspaceService)
    {
        _database = new SqlitePluginRegistryDatabase(
            workspaceService ?? throw new ArgumentNullException(nameof(workspaceService)));
    }

    public async Task UpsertAsync(PluginRecord record, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(record);
        ValidateRecord(record);

        await using var connection = await _database.OpenConnectionAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText =
            """
            INSERT INTO plugins (
                id,
                manifest_json,
                types_csv,
                install_source,
                root_path,
                trust_state,
                enabled,
                runtime_state,
                discovered_at,
                installed_at,
                updated_at,
                last_started_at,
                last_stopped_at,
                last_health_at,
                restart_count,
                last_error,
                trust_evidence_json
            )
            VALUES (
                $id,
                $manifestJson,
                $typesCsv,
                $installSource,
                $rootPath,
                $trustState,
                $enabled,
                $runtimeState,
                $discoveredAt,
                $installedAt,
                $updatedAt,
                $lastStartedAt,
                $lastStoppedAt,
                $lastHealthAt,
                $restartCount,
                $lastError,
                $trustEvidenceJson
            )
            ON CONFLICT(id) DO UPDATE SET
                manifest_json = excluded.manifest_json,
                types_csv = excluded.types_csv,
                install_source = excluded.install_source,
                root_path = excluded.root_path,
                trust_state = excluded.trust_state,
                enabled = excluded.enabled,
                runtime_state = excluded.runtime_state,
                discovered_at = excluded.discovered_at,
                installed_at = excluded.installed_at,
                updated_at = excluded.updated_at,
                last_started_at = excluded.last_started_at,
                last_stopped_at = excluded.last_stopped_at,
                last_health_at = excluded.last_health_at,
                restart_count = excluded.restart_count,
                last_error = excluded.last_error,
                trust_evidence_json = excluded.trust_evidence_json;
            """;

        BindRecord(command, record);
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    public async Task<PluginRecord?> GetByIdAsync(string id, CancellationToken cancellationToken = default)
    {
        ValidateId(id, nameof(id));

        await using var connection = await _database.OpenConnectionAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText =
            """
            SELECT
                id,
                manifest_json,
                install_source,
                root_path,
                trust_state,
                enabled,
                runtime_state,
                discovered_at,
                installed_at,
                updated_at,
                last_started_at,
                last_stopped_at,
                last_health_at,
                restart_count,
                last_error,
                trust_evidence_json
            FROM plugins
            WHERE id = $id
            LIMIT 1;
            """;
        command.Parameters.AddWithValue("$id", id);

        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        if (!await reader.ReadAsync(cancellationToken))
        {
            return null;
        }

        return MapRecord(reader);
    }

    public async Task<IReadOnlyList<PluginRecord>> ListAsync(
        PluginQuery? query = null,
        CancellationToken cancellationToken = default)
    {
        query ??= new PluginQuery();

        await using var connection = await _database.OpenConnectionAsync(cancellationToken);
        await using var command = connection.CreateCommand();

        var sql = new StringBuilder(
            """
            SELECT
                id,
                manifest_json,
                install_source,
                root_path,
                trust_state,
                enabled,
                runtime_state,
                discovered_at,
                installed_at,
                updated_at,
                last_started_at,
                last_stopped_at,
                last_health_at,
                restart_count,
                last_error,
                trust_evidence_json
            FROM plugins
            """);

        var filters = new List<string>();
        if (query.Type is { } type)
        {
            filters.Add("types_csv LIKE $typePattern");
            command.Parameters.AddWithValue("$typePattern", BuildTypePattern(type));
        }

        if (query.TrustState is { } trustState)
        {
            filters.Add("trust_state = $trustState");
            command.Parameters.AddWithValue("$trustState", trustState.ToString());
        }

        if (query.Enabled is { } enabled)
        {
            filters.Add("enabled = $enabled");
            command.Parameters.AddWithValue("$enabled", enabled ? 1 : 0);
        }

        if (query.RuntimeState is { } runtimeState)
        {
            filters.Add("runtime_state = $runtimeState");
            command.Parameters.AddWithValue("$runtimeState", runtimeState.ToString());
        }

        if (filters.Count > 0)
        {
            sql.Append(" WHERE ");
            sql.AppendJoin(" AND ", filters);
        }

        sql.Append(" ORDER BY updated_at DESC, installed_at DESC LIMIT $limit OFFSET $offset;");
        command.Parameters.AddWithValue("$limit", NormalizeLimit(query.Limit));
        command.Parameters.AddWithValue("$offset", Math.Max(0, query.Offset));
        command.CommandText = sql.ToString();

        var records = new List<PluginRecord>();
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            records.Add(MapRecord(reader));
        }

        return records;
    }

    public async Task<bool> DeleteAsync(string id, CancellationToken cancellationToken = default)
    {
        ValidateId(id, nameof(id));

        await using var connection = await _database.OpenConnectionAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText =
            """
            DELETE FROM plugins
            WHERE id = $id;
            """;
        command.Parameters.AddWithValue("$id", id);

        var rows = await command.ExecuteNonQueryAsync(cancellationToken);
        return rows > 0;
    }

    private static void BindRecord(SqliteCommand command, PluginRecord record)
    {
        var manifestJson = JsonSerializer.Serialize(record.Manifest, JsonOptions);
        var trustEvidenceJson = record.TrustEvidence is null
            ? null
            : JsonSerializer.Serialize(record.TrustEvidence, JsonOptions);

        command.Parameters.AddWithValue("$id", record.Id);
        command.Parameters.AddWithValue("$manifestJson", manifestJson);
        command.Parameters.AddWithValue("$typesCsv", BuildTypesCsv(record.Manifest.Types));
        command.Parameters.AddWithValue("$installSource", record.InstallSource.ToString());
        command.Parameters.AddWithValue("$rootPath", record.RootPath);
        command.Parameters.AddWithValue("$trustState", record.TrustState.ToString());
        command.Parameters.AddWithValue("$enabled", record.Enabled ? 1 : 0);
        command.Parameters.AddWithValue("$runtimeState", record.RuntimeState.ToString());
        command.Parameters.AddWithValue("$discoveredAt", FormatTimestamp(record.DiscoveredAt));
        command.Parameters.AddWithValue("$installedAt", FormatTimestamp(record.InstalledAt));
        command.Parameters.AddWithValue("$updatedAt", FormatTimestamp(record.UpdatedAt));
        command.Parameters.AddWithValue("$lastStartedAt", (object?)FormatTimestampOrNull(record.LastStartedAt) ?? DBNull.Value);
        command.Parameters.AddWithValue("$lastStoppedAt", (object?)FormatTimestampOrNull(record.LastStoppedAt) ?? DBNull.Value);
        command.Parameters.AddWithValue("$lastHealthAt", (object?)FormatTimestampOrNull(record.LastHealthAt) ?? DBNull.Value);
        command.Parameters.AddWithValue("$restartCount", record.RestartCount);
        command.Parameters.AddWithValue("$lastError", (object?)NormalizeNullableText(record.LastError) ?? DBNull.Value);
        command.Parameters.AddWithValue("$trustEvidenceJson", (object?)trustEvidenceJson ?? DBNull.Value);
    }

    private static PluginRecord MapRecord(SqliteDataReader reader)
    {
        var id = reader.GetString(0);
        var manifest = JsonSerializer.Deserialize<PluginManifest>(reader.GetString(1), JsonOptions)
            ?? throw new InvalidOperationException($"Plugin manifest payload is invalid for '{id}'.");
        var trustEvidenceJson = reader.IsDBNull(15) ? null : reader.GetString(15);
        var trustEvidence = string.IsNullOrWhiteSpace(trustEvidenceJson)
            ? null
            : JsonSerializer.Deserialize<PluginTrustEvidence>(trustEvidenceJson, JsonOptions);

        return new PluginRecord(
            Id: id,
            Manifest: manifest,
            InstallSource: ParseEnum<PluginInstallSource>(reader.GetString(2)),
            RootPath: reader.GetString(3),
            TrustState: ParseEnum<PluginTrustState>(reader.GetString(4)),
            Enabled: reader.GetInt64(5) != 0,
            RuntimeState: ParseEnum<PluginRuntimeState>(reader.GetString(6)),
            DiscoveredAt: ParseTimestamp(reader.GetString(7)),
            InstalledAt: ParseTimestamp(reader.GetString(8)),
            UpdatedAt: ParseTimestamp(reader.GetString(9)),
            LastStartedAt: reader.IsDBNull(10) ? null : ParseTimestamp(reader.GetString(10)),
            LastStoppedAt: reader.IsDBNull(11) ? null : ParseTimestamp(reader.GetString(11)),
            LastHealthAt: reader.IsDBNull(12) ? null : ParseTimestamp(reader.GetString(12)),
            RestartCount: reader.GetInt32(13),
            LastError: reader.IsDBNull(14) ? null : reader.GetString(14),
            TrustEvidence: trustEvidence);
    }

    private static string BuildTypesCsv(IReadOnlyList<PluginType> types)
    {
        if (types.Count == 0)
        {
            return ",";
        }

        var normalized = types
            .Distinct()
            .Select(static type => type.ToString())
            .ToArray();

        return $",{string.Join(",", normalized)},";
    }

    private static string BuildTypePattern(PluginType type)
    {
        return $"%,{type},%";
    }

    private static int NormalizeLimit(int limit)
    {
        if (limit <= 0)
        {
            return DefaultLimit;
        }

        return Math.Min(limit, MaxLimit);
    }

    private static string FormatTimestamp(DateTimeOffset value)
    {
        return value.ToString("O", CultureInfo.InvariantCulture);
    }

    private static string? FormatTimestampOrNull(DateTimeOffset? value)
    {
        return value is null ? null : FormatTimestamp(value.Value);
    }

    private static DateTimeOffset ParseTimestamp(string rawValue)
    {
        return DateTimeOffset.Parse(rawValue, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind);
    }

    private static TEnum ParseEnum<TEnum>(string rawValue)
        where TEnum : struct, Enum
    {
        return Enum.Parse<TEnum>(rawValue, ignoreCase: false);
    }

    private static string? NormalizeNullableText(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        return value.Trim();
    }

    private static void ValidateRecord(PluginRecord record)
    {
        ValidateId(record.Id, nameof(record));

        if (!string.Equals(record.Id, record.Manifest.Id, StringComparison.Ordinal))
        {
            throw new ArgumentException("Plugin id must match manifest id.", nameof(record));
        }

        if (string.IsNullOrWhiteSpace(record.RootPath))
        {
            throw new ArgumentException("Plugin root path is required.", nameof(record));
        }

        if (record.DiscoveredAt == default)
        {
            throw new ArgumentException("Discovered timestamp is required.", nameof(record));
        }

        if (record.InstalledAt == default)
        {
            throw new ArgumentException("Installed timestamp is required.", nameof(record));
        }

        if (record.UpdatedAt == default)
        {
            throw new ArgumentException("Updated timestamp is required.", nameof(record));
        }

        if (record.RestartCount < 0)
        {
            throw new ArgumentException("Restart count cannot be negative.", nameof(record));
        }
    }

    private static void ValidateId(string id, string paramName)
    {
        if (string.IsNullOrWhiteSpace(id))
        {
            throw new ArgumentException("Plugin id is required.", paramName);
        }
    }
}
