using System.Globalization;
using KodaClaw.Contracts;
using Microsoft.Data.Sqlite;

namespace KodaClaw.PluginHost.Diagnostics;

public sealed class SqlitePluginLogRepository : IPluginLogRepository
{
    private const int DefaultLimit = 200;
    private const int MaxLimit = 200;

    private readonly SqlitePluginLogDatabase _database;

    public SqlitePluginLogRepository(IWorkspaceService workspaceService)
    {
        _database = new SqlitePluginLogDatabase(
            workspaceService ?? throw new ArgumentNullException(nameof(workspaceService)));
    }

    public async Task AppendAsync(PluginLogEntry entry, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(entry);
        ValidateEntry(entry);

        await using var connection = await _database.OpenConnectionAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText =
            """
            INSERT INTO plugin_logs (
                entry_id,
                plugin_id,
                level,
                source,
                message,
                timestamp,
                payload_json
            )
            VALUES (
                $entryId,
                $pluginId,
                $level,
                $source,
                $message,
                $timestamp,
                $payloadJson
            )
            ON CONFLICT(entry_id) DO NOTHING;
            """;

        BindEntry(command, entry);
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    public async Task<IReadOnlyList<PluginLogEntry>> ListAsync(
        string pluginId,
        int limit = 200,
        CancellationToken cancellationToken = default)
    {
        ValidatePluginId(pluginId, nameof(pluginId));

        await using var connection = await _database.OpenConnectionAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText =
            """
            SELECT
                entry_id,
                plugin_id,
                level,
                source,
                message,
                timestamp,
                payload_json
            FROM plugin_logs
            WHERE plugin_id = $pluginId
            ORDER BY timestamp DESC, entry_id DESC
            LIMIT $limit;
            """;
        command.Parameters.AddWithValue("$pluginId", pluginId);
        command.Parameters.AddWithValue("$limit", NormalizeLimit(limit));

        var entries = new List<PluginLogEntry>();
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            entries.Add(MapEntry(reader));
        }

        return entries;
    }

    private static void BindEntry(SqliteCommand command, PluginLogEntry entry)
    {
        command.Parameters.AddWithValue("$entryId", entry.EntryId);
        command.Parameters.AddWithValue("$pluginId", entry.PluginId);
        command.Parameters.AddWithValue("$level", entry.Level);
        command.Parameters.AddWithValue("$source", entry.Source);
        command.Parameters.AddWithValue("$message", entry.Message);
        command.Parameters.AddWithValue("$timestamp", FormatTimestamp(entry.Timestamp));
        command.Parameters.AddWithValue("$payloadJson", (object?)NormalizeNullableText(entry.PayloadJson) ?? DBNull.Value);
    }

    private static PluginLogEntry MapEntry(SqliteDataReader reader)
    {
        return new PluginLogEntry(
            EntryId: reader.GetString(0),
            PluginId: reader.GetString(1),
            Level: reader.GetString(2),
            Source: reader.GetString(3),
            Message: reader.GetString(4),
            Timestamp: ParseTimestamp(reader.GetString(5)),
            PayloadJson: reader.IsDBNull(6) ? null : reader.GetString(6));
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

    private static DateTimeOffset ParseTimestamp(string rawValue)
    {
        return DateTimeOffset.Parse(rawValue, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind);
    }

    private static string? NormalizeNullableText(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        return value.Trim();
    }

    private static void ValidatePluginId(string pluginId, string paramName)
    {
        if (string.IsNullOrWhiteSpace(pluginId))
        {
            throw new ArgumentException("Plugin id is required.", paramName);
        }
    }

    private static void ValidateEntry(PluginLogEntry entry)
    {
        if (string.IsNullOrWhiteSpace(entry.EntryId))
        {
            throw new ArgumentException("Entry id is required.", nameof(entry));
        }

        ValidatePluginId(entry.PluginId, nameof(entry));

        if (string.IsNullOrWhiteSpace(entry.Level))
        {
            throw new ArgumentException("Log level is required.", nameof(entry));
        }

        if (string.IsNullOrWhiteSpace(entry.Source))
        {
            throw new ArgumentException("Log source is required.", nameof(entry));
        }

        if (string.IsNullOrWhiteSpace(entry.Message))
        {
            throw new ArgumentException("Log message is required.", nameof(entry));
        }

        if (entry.Timestamp == default)
        {
            throw new ArgumentException("Timestamp is required.", nameof(entry));
        }
    }
}
