using System.Globalization;
using System.Text;
using KodaClaw.Contracts;
using Microsoft.Data.Sqlite;

namespace KodaClaw.ControlPlane;

public sealed class SqliteApprovalRepository : IApprovalRepository
{
    private const int DefaultLimit = 50;
    private const int MaxLimit = 200;

    private readonly SemaphoreSlim _initializationLock = new(1, 1);
    private readonly IWorkspaceService _workspaceService;
    private string? _databasePath;
    private bool _initialized;

    public SqliteApprovalRepository(IWorkspaceService workspaceService)
    {
        _workspaceService = workspaceService ?? throw new ArgumentNullException(nameof(workspaceService));
    }

    public async Task UpsertAsync(Approval approval, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(approval);
        ValidateApproval(approval);

        await using var connection = await OpenConnectionAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText =
            """
            INSERT INTO approvals (
                id,
                kind,
                status,
                title,
                summary,
                source,
                requested_at,
                updated_at,
                session_id,
                correlation_id,
                inbox_item_id,
                payload_json,
                decided_at,
                decided_by,
                decision_note
            )
            VALUES (
                $id,
                $kind,
                $status,
                $title,
                $summary,
                $source,
                $requestedAt,
                $updatedAt,
                $sessionId,
                $correlationId,
                $inboxItemId,
                $payloadJson,
                $decidedAt,
                $decidedBy,
                $decisionNote
            )
            ON CONFLICT(id) DO UPDATE SET
                kind = excluded.kind,
                status = excluded.status,
                title = excluded.title,
                summary = excluded.summary,
                source = excluded.source,
                requested_at = excluded.requested_at,
                updated_at = excluded.updated_at,
                session_id = excluded.session_id,
                correlation_id = excluded.correlation_id,
                inbox_item_id = excluded.inbox_item_id,
                payload_json = excluded.payload_json,
                decided_at = excluded.decided_at,
                decided_by = excluded.decided_by,
                decision_note = excluded.decision_note;
            """;

        BindApprovalParameters(command, approval);
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    public async Task<Approval?> GetByIdAsync(string id, CancellationToken cancellationToken = default)
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
                requested_at,
                updated_at,
                session_id,
                correlation_id,
                inbox_item_id,
                payload_json,
                decided_at,
                decided_by,
                decision_note
            FROM approvals
            WHERE id = $id
            LIMIT 1;
            """;
        command.Parameters.AddWithValue("$id", id);

        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        if (!await reader.ReadAsync(cancellationToken))
        {
            return null;
        }

        return MapApproval(reader);
    }

    public async Task<IReadOnlyList<Approval>> ListAsync(
        ApprovalQuery? query = null,
        CancellationToken cancellationToken = default)
    {
        query ??= new ApprovalQuery();

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
                requested_at,
                updated_at,
                session_id,
                correlation_id,
                inbox_item_id,
                payload_json,
                decided_at,
                decided_by,
                decision_note
            FROM approvals
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

        sql.Append(" ORDER BY updated_at DESC, requested_at DESC LIMIT $limit;");
        command.Parameters.AddWithValue("$limit", NormalizeLimit(query.Limit));
        command.CommandText = sql.ToString();

        var approvals = new List<Approval>();
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            approvals.Add(MapApproval(reader));
        }

        return approvals;
    }

    public async Task<bool> TransitionAsync(
        string id,
        ApprovalStatus status,
        DateTimeOffset updatedAt,
        string? decidedBy = null,
        string? decisionNote = null,
        CancellationToken cancellationToken = default)
    {
        ValidateId(id);
        if (updatedAt == default)
        {
            throw new ArgumentException("Updated timestamp is required.", nameof(updatedAt));
        }

        if (status is not (ApprovalStatus.Approved or ApprovalStatus.Rejected or ApprovalStatus.Canceled))
        {
            throw new ArgumentException("Transition target must be a terminal status.", nameof(status));
        }

        await using var connection = await OpenConnectionAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText =
            """
            UPDATE approvals
            SET
                status = $status,
                updated_at = $updatedAt,
                decided_at = $decidedAt,
                decided_by = $decidedBy,
                decision_note = $decisionNote
            WHERE id = $id
                AND status = $pendingStatus;
            """;
        command.Parameters.AddWithValue("$id", id);
        command.Parameters.AddWithValue("$status", status.ToString());
        command.Parameters.AddWithValue("$updatedAt", FormatTimestamp(updatedAt));
        command.Parameters.AddWithValue("$decidedAt", FormatTimestamp(updatedAt));
        command.Parameters.AddWithValue("$decidedBy", (object?)decidedBy ?? DBNull.Value);
        command.Parameters.AddWithValue("$decisionNote", (object?)decisionNote ?? DBNull.Value);
        command.Parameters.AddWithValue("$pendingStatus", ApprovalStatus.Pending.ToString());

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
                CREATE TABLE IF NOT EXISTS approvals (
                    id TEXT NOT NULL PRIMARY KEY,
                    kind TEXT NOT NULL,
                    status TEXT NOT NULL,
                    title TEXT NOT NULL,
                    summary TEXT NOT NULL,
                    source TEXT NOT NULL,
                    requested_at TEXT NOT NULL,
                    updated_at TEXT NOT NULL,
                    session_id TEXT NULL,
                    correlation_id TEXT NULL,
                    inbox_item_id TEXT NULL,
                    payload_json TEXT NULL,
                    decided_at TEXT NULL,
                    decided_by TEXT NULL,
                    decision_note TEXT NULL
                );

                CREATE INDEX IF NOT EXISTS ix_approvals_status_updated_at
                    ON approvals(status, updated_at DESC, requested_at DESC);

                CREATE INDEX IF NOT EXISTS ix_approvals_kind_updated_at
                    ON approvals(kind, updated_at DESC, requested_at DESC);

                CREATE INDEX IF NOT EXISTS ix_approvals_session_id_updated_at
                    ON approvals(session_id, updated_at DESC, requested_at DESC);
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

    private static void BindApprovalParameters(SqliteCommand command, Approval approval)
    {
        command.Parameters.AddWithValue("$id", approval.Id);
        command.Parameters.AddWithValue("$kind", approval.Kind.ToString());
        command.Parameters.AddWithValue("$status", approval.Status.ToString());
        command.Parameters.AddWithValue("$title", approval.Title);
        command.Parameters.AddWithValue("$summary", approval.Summary);
        command.Parameters.AddWithValue("$source", approval.Source);
        command.Parameters.AddWithValue("$requestedAt", FormatTimestamp(approval.RequestedAt));
        command.Parameters.AddWithValue("$updatedAt", FormatTimestamp(approval.UpdatedAt));
        command.Parameters.AddWithValue("$sessionId", (object?)approval.SessionId ?? DBNull.Value);
        command.Parameters.AddWithValue("$correlationId", (object?)approval.CorrelationId ?? DBNull.Value);
        command.Parameters.AddWithValue("$inboxItemId", (object?)approval.InboxItemId ?? DBNull.Value);
        command.Parameters.AddWithValue("$payloadJson", (object?)approval.PayloadJson ?? DBNull.Value);
        command.Parameters.AddWithValue("$decidedAt", (object?)FormatTimestampOrNull(approval.DecidedAt) ?? DBNull.Value);
        command.Parameters.AddWithValue("$decidedBy", (object?)approval.DecidedBy ?? DBNull.Value);
        command.Parameters.AddWithValue("$decisionNote", (object?)approval.DecisionNote ?? DBNull.Value);
    }

    private static Approval MapApproval(SqliteDataReader reader)
    {
        return new Approval(
            Id: reader.GetString(0),
            Kind: ParseEnum<ApprovalKind>(reader.GetString(1)),
            Status: ParseEnum<ApprovalStatus>(reader.GetString(2)),
            Title: reader.GetString(3),
            Summary: reader.GetString(4),
            Source: reader.GetString(5),
            RequestedAt: ParseTimestamp(reader.GetString(6)),
            UpdatedAt: ParseTimestamp(reader.GetString(7)),
            SessionId: reader.IsDBNull(8) ? null : reader.GetString(8),
            CorrelationId: reader.IsDBNull(9) ? null : reader.GetString(9),
            InboxItemId: reader.IsDBNull(10) ? null : reader.GetString(10),
            PayloadJson: reader.IsDBNull(11) ? null : reader.GetString(11),
            DecidedAt: reader.IsDBNull(12) ? null : ParseTimestamp(reader.GetString(12)),
            DecidedBy: reader.IsDBNull(13) ? null : reader.GetString(13),
            DecisionNote: reader.IsDBNull(14) ? null : reader.GetString(14));
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

    private static void ValidateApproval(Approval approval)
    {
        ValidateId(approval.Id);

        if (string.IsNullOrWhiteSpace(approval.Title))
        {
            throw new ArgumentException("Title is required.", nameof(approval));
        }

        if (string.IsNullOrWhiteSpace(approval.Summary))
        {
            throw new ArgumentException("Summary is required.", nameof(approval));
        }

        if (string.IsNullOrWhiteSpace(approval.Source))
        {
            throw new ArgumentException("Source is required.", nameof(approval));
        }

        if (approval.RequestedAt == default)
        {
            throw new ArgumentException("Requested timestamp is required.", nameof(approval));
        }

        if (approval.UpdatedAt == default)
        {
            throw new ArgumentException("Updated timestamp is required.", nameof(approval));
        }
    }

    private static void ValidateId(string id)
    {
        if (string.IsNullOrWhiteSpace(id))
        {
            throw new ArgumentException("Id is required.", nameof(id));
        }
    }
}
