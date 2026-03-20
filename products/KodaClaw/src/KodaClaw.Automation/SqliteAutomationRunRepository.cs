using System.Globalization;
using System.Text;
using KodaClaw.Contracts;
using Microsoft.Data.Sqlite;

namespace KodaClaw.Automation;

public sealed class SqliteAutomationRunRepository : IAutomationRunRepository
{
    private readonly SqliteAutomationDatabase _database;

    internal SqliteAutomationRunRepository(SqliteAutomationDatabase database)
    {
        _database = database ?? throw new ArgumentNullException(nameof(database));
    }

    public SqliteAutomationRunRepository(IWorkspaceService workspaceService)
    {
        _database = new SqliteAutomationDatabase(workspaceService ?? throw new ArgumentNullException(nameof(workspaceService)));
    }

    public async Task AddAsync(AutomationRunRecord runRecord, CancellationToken cancellationToken = default)
    {
        AutomationValidation.ValidateRunRecord(runRecord);

        await using var connection = await _database.OpenConnectionAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText =
            """
            INSERT INTO automation_runs (
                run_id,
                automation_id,
                status,
                trigger,
                attempt,
                session_id,
                started_at,
                completed_at,
                summary,
                error_message
            )
            VALUES (
                $runId,
                $automationId,
                $status,
                $trigger,
                $attempt,
                $sessionId,
                $startedAt,
                $completedAt,
                $summary,
                $errorMessage
            );
            """;
        BindRunRecord(command, runRecord);
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    public async Task<bool> UpdateAsync(AutomationRunRecord runRecord, CancellationToken cancellationToken = default)
    {
        AutomationValidation.ValidateRunRecord(runRecord);

        await using var connection = await _database.OpenConnectionAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText =
            """
            UPDATE automation_runs
            SET
                automation_id = $automationId,
                status = $status,
                trigger = $trigger,
                attempt = $attempt,
                session_id = $sessionId,
                started_at = $startedAt,
                completed_at = $completedAt,
                summary = $summary,
                error_message = $errorMessage
            WHERE run_id = $runId;
            """;
        BindRunRecord(command, runRecord);

        var rows = await command.ExecuteNonQueryAsync(cancellationToken);
        return rows > 0;
    }

    public async Task<IReadOnlyList<AutomationRunRecord>> ListAsync(
        AutomationRunQuery? query = null,
        CancellationToken cancellationToken = default)
    {
        query ??= new AutomationRunQuery();

        await using var connection = await _database.OpenConnectionAsync(cancellationToken);
        await using var command = connection.CreateCommand();

        var sql = new StringBuilder(
            """
            SELECT
                run_id,
                automation_id,
                status,
                trigger,
                attempt,
                session_id,
                started_at,
                completed_at,
                summary,
                error_message
            FROM automation_runs
            """);

        var filters = new List<string>();
        if (!string.IsNullOrWhiteSpace(query.AutomationId))
        {
            filters.Add("automation_id = $automationId");
            command.Parameters.AddWithValue("$automationId", query.AutomationId.Trim());
        }

        if (query.Status is { } status)
        {
            filters.Add("status = $status");
            command.Parameters.AddWithValue("$status", status.ToString());
        }

        if (filters.Count > 0)
        {
            sql.Append(" WHERE ");
            sql.AppendJoin(" AND ", filters);
        }

        sql.Append(" ORDER BY started_at DESC, run_id DESC LIMIT $limit;");
        command.Parameters.AddWithValue("$limit", AutomationValidation.NormalizeLimit(query.Limit));
        command.CommandText = sql.ToString();

        var list = new List<AutomationRunRecord>();
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            list.Add(MapRunRecord(reader));
        }

        return list;
    }

    public async Task<AutomationRunRecord?> GetByIdAsync(string runId, CancellationToken cancellationToken = default)
    {
        AutomationValidation.ValidateId(runId, nameof(runId));

        await using var connection = await _database.OpenConnectionAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText =
            """
            SELECT
                run_id,
                automation_id,
                status,
                trigger,
                attempt,
                session_id,
                started_at,
                completed_at,
                summary,
                error_message
            FROM automation_runs
            WHERE run_id = $runId
            LIMIT 1;
            """;
        command.Parameters.AddWithValue("$runId", runId);

        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        if (!await reader.ReadAsync(cancellationToken))
        {
            return null;
        }

        return MapRunRecord(reader);
    }

    private static void BindRunRecord(SqliteCommand command, AutomationRunRecord runRecord)
    {
        command.Parameters.AddWithValue("$runId", runRecord.RunId);
        command.Parameters.AddWithValue("$automationId", runRecord.AutomationId);
        command.Parameters.AddWithValue("$status", runRecord.Status.ToString());
        command.Parameters.AddWithValue("$trigger", runRecord.Trigger);
        command.Parameters.AddWithValue("$attempt", runRecord.Attempt);
        command.Parameters.AddWithValue("$sessionId", (object?)NormalizeNullableText(runRecord.SessionId) ?? DBNull.Value);
        command.Parameters.AddWithValue("$startedAt", FormatTimestamp(runRecord.StartedAt));
        command.Parameters.AddWithValue("$completedAt", (object?)FormatTimestampOrNull(runRecord.CompletedAt) ?? DBNull.Value);
        command.Parameters.AddWithValue("$summary", (object?)NormalizeNullableText(runRecord.Summary) ?? DBNull.Value);
        command.Parameters.AddWithValue("$errorMessage", (object?)NormalizeNullableText(runRecord.ErrorMessage) ?? DBNull.Value);
    }

    private static AutomationRunRecord MapRunRecord(SqliteDataReader reader)
    {
        return new AutomationRunRecord(
            RunId: reader.GetString(0),
            AutomationId: reader.GetString(1),
            Status: ParseEnum<AutomationRunStatus>(reader.GetString(2)),
            Trigger: reader.GetString(3),
            Attempt: reader.GetInt32(4),
            SessionId: reader.IsDBNull(5) ? null : reader.GetString(5),
            StartedAt: ParseTimestamp(reader.GetString(6)),
            CompletedAt: reader.IsDBNull(7) ? null : ParseTimestamp(reader.GetString(7)),
            Summary: reader.IsDBNull(8) ? null : reader.GetString(8),
            ErrorMessage: reader.IsDBNull(9) ? null : reader.GetString(9));
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
