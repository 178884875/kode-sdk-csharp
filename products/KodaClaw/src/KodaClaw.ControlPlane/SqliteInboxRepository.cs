using System.Globalization;
using System.Text;
using KodaClaw.Contracts;
using Microsoft.Data.Sqlite;

namespace KodaClaw.ControlPlane;

public sealed class SqliteInboxRepository : IInboxRepository
{
    private const int DefaultLimit = 50;
    private const int MaxLimit = 200;
    private readonly SemaphoreSlim _initializationLock = new(1, 1);
    private readonly IWorkspaceService _workspaceService;
    private string? _databasePath;
    private bool _initialized;

    public SqliteInboxRepository(IWorkspaceService workspaceService)
    {
        _workspaceService = workspaceService ?? throw new ArgumentNullException(nameof(workspaceService));
    }

    public async Task UpsertAsync(InboxItem item, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(item);
        ValidateItem(item);

        await using var connection = await OpenConnectionAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText =
            """
            INSERT INTO inbox_items (
                id,
                kind,
                status,
                title,
                summary,
                source,
                requires_action,
                route,
                session_id,
                correlation_id,
                approval_id,
                payload_json,
                created_at,
                updated_at,
                resolved_at
            )
            VALUES (
                $id,
                $kind,
                $status,
                $title,
                $summary,
                $source,
                $requiresAction,
                $route,
                $sessionId,
                $correlationId,
                $approvalId,
                $payloadJson,
                $createdAt,
                $updatedAt,
                $resolvedAt
            )
            ON CONFLICT(id) DO UPDATE SET
                kind = excluded.kind,
                status = excluded.status,
                title = excluded.title,
                summary = excluded.summary,
                source = excluded.source,
                requires_action = excluded.requires_action,
                route = excluded.route,
                session_id = excluded.session_id,
                correlation_id = excluded.correlation_id,
                approval_id = excluded.approval_id,
                payload_json = excluded.payload_json,
                created_at = excluded.created_at,
                updated_at = excluded.updated_at,
                resolved_at = excluded.resolved_at;
            """;

        BindItemParameters(command, item);
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    public async Task<InboxItem?> GetByIdAsync(string id, CancellationToken cancellationToken = default)
    {
        ValidateId(id);

        await using var connection = await OpenConnectionAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText =
            """
            SELECT
                id,
                kind,
                status,
                title,
                summary,
                source,
                requires_action,
                route,
                session_id,
                correlation_id,
                approval_id,
                payload_json,
                created_at,
                updated_at,
                resolved_at
            FROM inbox_items
            WHERE id = $id
            LIMIT 1;
            """;
        command.Parameters.AddWithValue("$id", id);

        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        if (!await reader.ReadAsync(cancellationToken))
        {
            return null;
        }

        return MapItem(reader);
    }

    public async Task<IReadOnlyList<InboxItem>> ListAsync(
        InboxQuery? query = null,
        CancellationToken cancellationToken = default)
    {
        query ??= new InboxQuery();

        await using var connection = await OpenConnectionAsync(cancellationToken);
        await using var command = connection.CreateCommand();

        var sql = new StringBuilder(
            """
            SELECT
                id,
                kind,
                status,
                title,
                summary,
                source,
                requires_action,
                route,
                session_id,
                correlation_id,
                approval_id,
                payload_json,
                created_at,
                updated_at,
                resolved_at
            FROM inbox_items
            """);

        var filters = new List<string>();
        if (query.Status is { } status)
        {
            filters.Add("status = $status");
            command.Parameters.AddWithValue("$status", status.ToString());
        }

        if (query.Kind is { } kind)
        {
            filters.Add("kind = $kind");
            command.Parameters.AddWithValue("$kind", kind.ToString());
        }

        if (query.RequiresAction is { } requiresAction)
        {
            filters.Add("requires_action = $requiresAction");
            command.Parameters.AddWithValue("$requiresAction", requiresAction ? 1 : 0);
        }

        if (!string.IsNullOrWhiteSpace(query.SessionId))
        {
            filters.Add("session_id = $sessionId");
            command.Parameters.AddWithValue("$sessionId", query.SessionId);
        }

        if (filters.Count > 0)
        {
            sql.Append(" WHERE ");
            sql.AppendJoin(" AND ", filters);
        }

        sql.Append(" ORDER BY updated_at DESC, created_at DESC LIMIT $limit;");
        command.Parameters.AddWithValue("$limit", NormalizeLimit(query.Limit));
        command.CommandText = sql.ToString();

        var items = new List<InboxItem>();
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            items.Add(MapItem(reader));
        }

        return items;
    }

    public async Task<bool> UpdateStatusAsync(
        string id,
        InboxItemStatus status,
        DateTimeOffset updatedAt,
        DateTimeOffset? resolvedAt = null,
        CancellationToken cancellationToken = default)
    {
        ValidateId(id);
        if (updatedAt == default)
        {
            throw new ArgumentException("Updated timestamp is required.", nameof(updatedAt));
        }

        var effectiveResolvedAt = status is InboxItemStatus.Resolved or InboxItemStatus.Archived
            ? resolvedAt ?? updatedAt
            : resolvedAt;

        await using var connection = await OpenConnectionAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText =
            """
            UPDATE inbox_items
            SET
                status = $status,
                updated_at = $updatedAt,
                resolved_at = $resolvedAt
            WHERE id = $id;
            """;
        command.Parameters.AddWithValue("$id", id);
        command.Parameters.AddWithValue("$status", status.ToString());
        command.Parameters.AddWithValue("$updatedAt", FormatTimestamp(updatedAt));
        command.Parameters.AddWithValue("$resolvedAt", (object?)FormatTimestampOrNull(effectiveResolvedAt) ?? DBNull.Value);

        var rows = await command.ExecuteNonQueryAsync(cancellationToken);
        return rows > 0;
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
                """
                CREATE TABLE IF NOT EXISTS inbox_items (
                    id TEXT NOT NULL PRIMARY KEY,
                    kind TEXT NOT NULL,
                    status TEXT NOT NULL,
                    title TEXT NOT NULL,
                    summary TEXT NOT NULL,
                    source TEXT NOT NULL,
                    requires_action INTEGER NOT NULL,
                    route TEXT NULL,
                    session_id TEXT NULL,
                    correlation_id TEXT NULL,
                    approval_id TEXT NULL,
                    payload_json TEXT NULL,
                    created_at TEXT NOT NULL,
                    updated_at TEXT NOT NULL,
                    resolved_at TEXT NULL
                );

                CREATE INDEX IF NOT EXISTS ix_inbox_items_status_updated_at
                    ON inbox_items(status, updated_at DESC, created_at DESC);

                CREATE INDEX IF NOT EXISTS ix_inbox_items_session_id_updated_at
                    ON inbox_items(session_id, updated_at DESC, created_at DESC);

                CREATE INDEX IF NOT EXISTS ix_inbox_items_requires_action_updated_at
                    ON inbox_items(requires_action, updated_at DESC, created_at DESC);
                """;
            await command.ExecuteNonQueryAsync(cancellationToken);

            _databasePath = databasePath;
            _initialized = true;
            return databasePath;
        }
        finally
        {
            _initializationLock.Release();
        }
    }

    private static void BindItemParameters(SqliteCommand command, InboxItem item)
    {
        command.Parameters.AddWithValue("$id", item.Id);
        command.Parameters.AddWithValue("$kind", item.Kind.ToString());
        command.Parameters.AddWithValue("$status", item.Status.ToString());
        command.Parameters.AddWithValue("$title", item.Title);
        command.Parameters.AddWithValue("$summary", item.Summary);
        command.Parameters.AddWithValue("$source", item.Source);
        command.Parameters.AddWithValue("$requiresAction", item.RequiresAction ? 1 : 0);
        command.Parameters.AddWithValue("$route", (object?)item.Route ?? DBNull.Value);
        command.Parameters.AddWithValue("$sessionId", (object?)item.SessionId ?? DBNull.Value);
        command.Parameters.AddWithValue("$correlationId", (object?)item.CorrelationId ?? DBNull.Value);
        command.Parameters.AddWithValue("$approvalId", (object?)item.ApprovalId ?? DBNull.Value);
        command.Parameters.AddWithValue("$payloadJson", (object?)item.PayloadJson ?? DBNull.Value);
        command.Parameters.AddWithValue("$createdAt", FormatTimestamp(item.CreatedAt));
        command.Parameters.AddWithValue("$updatedAt", FormatTimestamp(item.UpdatedAt));
        command.Parameters.AddWithValue("$resolvedAt", (object?)FormatTimestampOrNull(item.ResolvedAt) ?? DBNull.Value);
    }

    private static InboxItem MapItem(SqliteDataReader reader)
    {
        return new InboxItem(
            Id: reader.GetString(0),
            Kind: ParseEnum<InboxItemKind>(reader.GetString(1)),
            Status: ParseEnum<InboxItemStatus>(reader.GetString(2)),
            Title: reader.GetString(3),
            Summary: reader.GetString(4),
            Source: reader.GetString(5),
            CreatedAt: ParseTimestamp(reader.GetString(12)),
            UpdatedAt: ParseTimestamp(reader.GetString(13)),
            RequiresAction: reader.GetInt64(6) != 0,
            Route: reader.IsDBNull(7) ? null : reader.GetString(7),
            SessionId: reader.IsDBNull(8) ? null : reader.GetString(8),
            CorrelationId: reader.IsDBNull(9) ? null : reader.GetString(9),
            ApprovalId: reader.IsDBNull(10) ? null : reader.GetString(10),
            PayloadJson: reader.IsDBNull(11) ? null : reader.GetString(11),
            ResolvedAt: reader.IsDBNull(14) ? null : ParseTimestamp(reader.GetString(14)));
    }

    private static TEnum ParseEnum<TEnum>(string rawValue)
        where TEnum : struct, Enum
    {
        return Enum.Parse<TEnum>(rawValue, ignoreCase: false);
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

    private static void ValidateItem(InboxItem item)
    {
        ValidateId(item.Id);

        if (string.IsNullOrWhiteSpace(item.Title))
        {
            throw new ArgumentException("Title is required.", nameof(item));
        }

        if (string.IsNullOrWhiteSpace(item.Source))
        {
            throw new ArgumentException("Source is required.", nameof(item));
        }

        if (item.CreatedAt == default)
        {
            throw new ArgumentException("Created timestamp is required.", nameof(item));
        }

        if (item.UpdatedAt == default)
        {
            throw new ArgumentException("Updated timestamp is required.", nameof(item));
        }
    }

    private static void ValidateId(string id)
    {
        if (string.IsNullOrWhiteSpace(id))
        {
            throw new ArgumentException("Inbox item id is required.", nameof(id));
        }
    }
}
