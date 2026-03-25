using System.Globalization;
using System.Text;
using System.Text.Json;
using KodaClaw.Contracts;
using Microsoft.Data.Sqlite;

namespace KodaClaw.Automation;

public sealed class SqliteAutomationDefinitionRepository : IAutomationDefinitionRepository
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);
    private readonly SqliteAutomationDatabase _database;

    internal SqliteAutomationDefinitionRepository(SqliteAutomationDatabase database)
    {
        _database = database ?? throw new ArgumentNullException(nameof(database));
    }

    public SqliteAutomationDefinitionRepository(IWorkspaceService workspaceService)
    {
        _database = new SqliteAutomationDatabase(workspaceService ?? throw new ArgumentNullException(nameof(workspaceService)));
    }

    public async Task UpsertAsync(AutomationDefinition definition, CancellationToken cancellationToken = default)
    {
        AutomationValidation.ValidateDefinition(definition);

        await using var connection = await _database.OpenConnectionAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText =
            """
            INSERT INTO automation_definitions (
                id,
                title,
                prompt,
                source,
                source_path,
                cron,
                enabled,
                input_paths,
                model_id,
                notification_channels,
                notify_mode,
                created_at,
                updated_at,
                last_run_at,
                next_run_at,
                last_run_status,
                last_error
            )
            VALUES (
                $id,
                $title,
                $prompt,
                $source,
                $sourcePath,
                $cron,
                $enabled,
                $inputPaths,
                $modelId,
                $notificationChannels,
                $notifyMode,
                $createdAt,
                $updatedAt,
                $lastRunAt,
                $nextRunAt,
                $lastRunStatus,
                $lastError
            )
            ON CONFLICT(id) DO UPDATE SET
                title = excluded.title,
                prompt = excluded.prompt,
                source = excluded.source,
                source_path = excluded.source_path,
                cron = excluded.cron,
                enabled = excluded.enabled,
                input_paths = excluded.input_paths,
                model_id = excluded.model_id,
                notification_channels = excluded.notification_channels,
                notify_mode = excluded.notify_mode,
                created_at = excluded.created_at,
                updated_at = excluded.updated_at,
                last_run_at = excluded.last_run_at,
                next_run_at = excluded.next_run_at,
                last_run_status = excluded.last_run_status,
                last_error = excluded.last_error;
            """;

        BindDefinition(command, definition);
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    public async Task<IReadOnlyList<AutomationDefinition>> ListAsync(
        AutomationDefinitionQuery? query = null,
        CancellationToken cancellationToken = default)
    {
        query ??= new AutomationDefinitionQuery();

        await using var connection = await _database.OpenConnectionAsync(cancellationToken);
        await using var command = connection.CreateCommand();

        var sql = new StringBuilder(
            """
            SELECT
                id,
                title,
                prompt,
                source,
                source_path,
                cron,
                enabled,
                input_paths,
                model_id,
                notification_channels,
                notify_mode,
                created_at,
                updated_at,
                last_run_at,
                next_run_at,
                last_run_status,
                last_error
            FROM automation_definitions
            """);

        var filters = new List<string>();
        if (query.Enabled is { } enabled)
        {
            filters.Add("enabled = $enabled");
            command.Parameters.AddWithValue("$enabled", enabled ? 1 : 0);
        }

        if (query.Source is { } source)
        {
            filters.Add("source = $source");
            command.Parameters.AddWithValue("$source", source.ToString());
        }

        if (filters.Count > 0)
        {
            sql.Append(" WHERE ");
            sql.AppendJoin(" AND ", filters);
        }

        sql.Append(" ORDER BY updated_at DESC, created_at DESC LIMIT $limit;");
        command.Parameters.AddWithValue("$limit", AutomationValidation.NormalizeLimit(query.Limit));
        command.CommandText = sql.ToString();

        var list = new List<AutomationDefinition>();
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            list.Add(MapDefinition(reader));
        }

        return list;
    }

    public async Task<AutomationDefinition?> GetByIdAsync(string id, CancellationToken cancellationToken = default)
    {
        AutomationValidation.ValidateId(id, nameof(id));

        await using var connection = await _database.OpenConnectionAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText =
            """
            SELECT
                id,
                title,
                prompt,
                source,
                source_path,
                cron,
                enabled,
                input_paths,
                model_id,
                notification_channels,
                notify_mode,
                created_at,
                updated_at,
                last_run_at,
                next_run_at,
                last_run_status,
                last_error
            FROM automation_definitions
            WHERE id = $id
            LIMIT 1;
            """;
        command.Parameters.AddWithValue("$id", id);

        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        if (!await reader.ReadAsync(cancellationToken))
        {
            return null;
        }

        return MapDefinition(reader);
    }

    public async Task<bool> DeleteAsync(string id, CancellationToken cancellationToken = default)
    {
        AutomationValidation.ValidateId(id, nameof(id));

        await using var connection = await _database.OpenConnectionAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText =
            """
            DELETE FROM automation_definitions
            WHERE id = $id;
            """;
        command.Parameters.AddWithValue("$id", id);

        var rows = await command.ExecuteNonQueryAsync(cancellationToken);
        return rows > 0;
    }

    private static void BindDefinition(SqliteCommand command, AutomationDefinition definition)
    {
        command.Parameters.AddWithValue("$id", definition.Id);
        command.Parameters.AddWithValue("$title", definition.Title);
        command.Parameters.AddWithValue("$prompt", definition.Prompt);
        command.Parameters.AddWithValue("$source", definition.Source.ToString());
        command.Parameters.AddWithValue("$sourcePath", (object?)definition.SourcePath ?? DBNull.Value);
        command.Parameters.AddWithValue("$cron", definition.CronExpression);
        command.Parameters.AddWithValue("$enabled", definition.Enabled ? 1 : 0);
        command.Parameters.AddWithValue("$inputPaths", (object?)SerializeTextList(definition.InputPaths) ?? DBNull.Value);
        command.Parameters.AddWithValue("$modelId", (object?)NormalizeNullableText(definition.ModelId) ?? DBNull.Value);
        command.Parameters.AddWithValue("$notificationChannels", (object?)SerializeTextList(definition.NotificationChannels) ?? DBNull.Value);
        command.Parameters.AddWithValue("$notifyMode", (int)definition.NotifyMode);
        command.Parameters.AddWithValue("$createdAt", FormatTimestamp(definition.CreatedAt));
        command.Parameters.AddWithValue("$updatedAt", FormatTimestamp(definition.UpdatedAt));
        command.Parameters.AddWithValue("$lastRunAt", (object?)FormatTimestampOrNull(definition.LastRunAt) ?? DBNull.Value);
        command.Parameters.AddWithValue("$nextRunAt", (object?)FormatTimestampOrNull(definition.NextRunAt) ?? DBNull.Value);
        command.Parameters.AddWithValue("$lastRunStatus", (object?)definition.LastRunStatus?.ToString() ?? DBNull.Value);
        command.Parameters.AddWithValue("$lastError", (object?)NormalizeNullableText(definition.LastError) ?? DBNull.Value);
    }

    private static AutomationDefinition MapDefinition(SqliteDataReader reader)
    {
        // Column order: 0=id, 1=title, 2=prompt, 3=source, 4=source_path,
        //               5=cron, 6=enabled, 7=input_paths, 8=model_id,
        //               9=notification_channels, 10=notify_mode,
        //               11=created_at, 12=updated_at, 13=last_run_at, 14=next_run_at,
        //               15=last_run_status, 16=last_error

        // For rows migrated from legacy schema where cron may be NULL, fall back to
        // a safe default so the application does not crash on existing data.
        var cronExpression = ReadNullableText(reader, 5) ?? "0 * * * *";

        return new AutomationDefinition(
            Id: reader.GetString(0),
            Title: reader.GetString(1),
            Prompt: reader.GetString(2),
            Source: ParseEnum<AutomationDefinitionSource>(reader.GetString(3)),
            SourcePath: ReadNullableText(reader, 4),
            CronExpression: cronExpression,
            Enabled: reader.GetInt64(6) != 0,
            InputPaths: DeserializeTextList(ReadNullableText(reader, 7)),
            ModelId: ReadNullableText(reader, 8),
            NotificationChannels: DeserializeTextList(ReadNullableText(reader, 9)),
            NotifyMode: (AutomationNotifyMode)reader.GetInt32(10),
            CreatedAt: ParseTimestamp(reader.GetString(11)),
            UpdatedAt: ParseTimestamp(reader.GetString(12)),
            LastRunAt: reader.IsDBNull(13) ? null : ParseTimestamp(reader.GetString(13)),
            NextRunAt: reader.IsDBNull(14) ? null : ParseTimestamp(reader.GetString(14)),
            LastRunStatus: reader.IsDBNull(15) ? null : ParseEnum<AutomationRunStatus>(reader.GetString(15)),
            LastError: ReadNullableText(reader, 16));
    }

    private static string? SerializeTextList(IReadOnlyList<string>? values)
    {
        if (values is not { Count: > 0 })
        {
            return null;
        }

        return JsonSerializer.Serialize(values, JsonOptions);
    }

    private static IReadOnlyList<string>? DeserializeTextList(string? rawValue)
    {
        if (string.IsNullOrWhiteSpace(rawValue))
        {
            return null;
        }

        var values = JsonSerializer.Deserialize<string[]>(rawValue, JsonOptions);
        return values is { Length: > 0 } ? values : null;
    }

    private static string? NormalizeNullableText(string? value)
    {
        if (value is null)
        {
            return null;
        }

        var normalized = value.Trim();
        return normalized.Length == 0 ? null : normalized;
    }

    private static string? ReadNullableText(SqliteDataReader reader, int ordinal)
    {
        return reader.IsDBNull(ordinal) ? null : reader.GetString(ordinal);
    }

    private static string FormatTimestamp(DateTimeOffset value)
    {
        return value.ToString("O", CultureInfo.InvariantCulture);
    }

    private static string? FormatTimestampOrNull(DateTimeOffset? value)
    {
        return value is { } timestamp ? FormatTimestamp(timestamp) : null;
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
}
