using KodaClaw.Contracts;
using Microsoft.Data.Sqlite;

namespace KodaClaw.ChannelHub;

public sealed class SqliteChannelAuditRepository : IChannelAuditRepository
{
    private readonly SemaphoreSlim _initializationLock = new(1, 1);
    private readonly SqliteChannelHubDatabase _database;
    private bool _initialized;

    public SqliteChannelAuditRepository(IWorkspaceService workspaceService)
    {
        _database = new SqliteChannelHubDatabase(
            workspaceService ?? throw new ArgumentNullException(nameof(workspaceService)));
    }

    public async Task AppendAsync(ChannelAuditEntry entry, CancellationToken cancellationToken = default)
    {
        ValidateAuditEntry(entry);
        await EnsureSchemaAsync(cancellationToken);

        await using var connection = await _database.OpenConnectionAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText =
            """
            INSERT INTO channel_audits (
                id,
                binding_id,
                connector_kind,
                account_id,
                external_thread_id,
                thread_type,
                event_type,
                created_at,
                session_id,
                approval_id,
                delivery_mode,
                external_message_id,
                summary,
                metadata_json
            )
            VALUES (
                $id,
                $bindingId,
                $connectorKind,
                $accountId,
                $externalThreadId,
                $threadType,
                $eventType,
                $createdAt,
                $sessionId,
                $approvalId,
                $deliveryMode,
                $externalMessageId,
                $summary,
                $metadataJson
            );
            """;

        BindEntry(command, entry);
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    public async Task<IReadOnlyList<ChannelAuditEntry>> ListByBindingIdAsync(
        string bindingId,
        int limit = 50,
        CancellationToken cancellationToken = default)
    {
        ChannelHubValidation.ValidateId(bindingId, nameof(bindingId));
        await EnsureSchemaAsync(cancellationToken);

        await using var connection = await _database.OpenConnectionAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText =
            """
            SELECT
                id,
                binding_id,
                connector_kind,
                account_id,
                external_thread_id,
                thread_type,
                event_type,
                created_at,
                session_id,
                approval_id,
                delivery_mode,
                external_message_id,
                summary,
                metadata_json
            FROM channel_audits
            WHERE binding_id = $bindingId
            ORDER BY created_at DESC, id DESC
            LIMIT $limit;
            """;
        command.Parameters.AddWithValue("$bindingId", bindingId.Trim());
        command.Parameters.AddWithValue("$limit", ChannelHubValidation.NormalizeLimit(limit));

        var items = new List<ChannelAuditEntry>();
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            items.Add(MapEntry(reader));
        }

        return items;
    }

    private async Task EnsureSchemaAsync(CancellationToken cancellationToken)
    {
        if (_initialized)
        {
            return;
        }

        await _initializationLock.WaitAsync(cancellationToken);
        try
        {
            if (_initialized)
            {
                return;
            }

            await using var connection = await _database.OpenConnectionAsync(cancellationToken);
            await using var command = connection.CreateCommand();
            command.CommandText =
                """
                CREATE TABLE IF NOT EXISTS channel_audits (
                    id TEXT NOT NULL PRIMARY KEY,
                    binding_id TEXT NOT NULL,
                    connector_kind TEXT NOT NULL,
                    account_id TEXT NOT NULL,
                    external_thread_id TEXT NOT NULL,
                    thread_type TEXT NOT NULL,
                    event_type TEXT NOT NULL,
                    created_at TEXT NOT NULL,
                    session_id TEXT NULL,
                    approval_id TEXT NULL,
                    delivery_mode TEXT NULL,
                    external_message_id TEXT NULL,
                    summary TEXT NULL,
                    metadata_json TEXT NULL
                );

                CREATE INDEX IF NOT EXISTS ix_channel_audits_binding_created
                    ON channel_audits(binding_id, created_at DESC, id DESC);
                """;
            await command.ExecuteNonQueryAsync(cancellationToken);

            _initialized = true;
        }
        finally
        {
            _initializationLock.Release();
        }
    }

    private static void ValidateAuditEntry(ChannelAuditEntry entry)
    {
        ArgumentNullException.ThrowIfNull(entry);
        ChannelHubValidation.ValidateId(entry.Id, nameof(entry.Id));
        ChannelHubValidation.ValidateId(entry.BindingId, nameof(entry.BindingId));
        ChannelHubValidation.ValidateId(entry.AccountId, nameof(entry.AccountId));
        ChannelHubValidation.ValidateId(entry.ExternalThreadId, nameof(entry.ExternalThreadId));
        ChannelHubValidation.ValidateId(entry.EventType, nameof(entry.EventType));

        if (entry.CreatedAt == default)
        {
            throw new ArgumentException("Audit entry created timestamp is required.", nameof(entry));
        }
    }

    private static void BindEntry(SqliteCommand command, ChannelAuditEntry entry)
    {
        command.Parameters.AddWithValue("$id", entry.Id);
        command.Parameters.AddWithValue("$bindingId", entry.BindingId);
        command.Parameters.AddWithValue("$connectorKind", entry.ConnectorKind.ToString());
        command.Parameters.AddWithValue("$accountId", entry.AccountId);
        command.Parameters.AddWithValue("$externalThreadId", entry.ExternalThreadId);
        command.Parameters.AddWithValue("$threadType", entry.ThreadType.ToString());
        command.Parameters.AddWithValue("$eventType", entry.EventType);
        command.Parameters.AddWithValue("$createdAt", ChannelHubValidation.FormatTimestamp(entry.CreatedAt));
        command.Parameters.AddWithValue("$sessionId", (object?)ChannelHubValidation.NormalizeNullableText(entry.SessionId) ?? DBNull.Value);
        command.Parameters.AddWithValue("$approvalId", (object?)ChannelHubValidation.NormalizeNullableText(entry.ApprovalId) ?? DBNull.Value);
        command.Parameters.AddWithValue("$deliveryMode", entry.DeliveryMode?.ToString() ?? (object)DBNull.Value);
        command.Parameters.AddWithValue("$externalMessageId", (object?)ChannelHubValidation.NormalizeNullableText(entry.ExternalMessageId) ?? DBNull.Value);
        command.Parameters.AddWithValue("$summary", (object?)ChannelHubValidation.NormalizeNullableText(entry.Summary) ?? DBNull.Value);
        command.Parameters.AddWithValue("$metadataJson", (object?)ChannelHubValidation.NormalizeNullableText(entry.MetadataJson) ?? DBNull.Value);
    }

    private static ChannelAuditEntry MapEntry(SqliteDataReader reader)
    {
        return new ChannelAuditEntry(
            Id: reader.GetString(0),
            BindingId: reader.GetString(1),
            ConnectorKind: ChannelHubValidation.ParseEnum<ChannelConnectorKind>(reader.GetString(2)),
            AccountId: reader.GetString(3),
            ExternalThreadId: reader.GetString(4),
            ThreadType: ChannelHubValidation.ParseEnum<ChannelThreadType>(reader.GetString(5)),
            EventType: reader.GetString(6),
            CreatedAt: ChannelHubValidation.ParseTimestamp(reader.GetString(7)),
            SessionId: reader.IsDBNull(8) ? null : reader.GetString(8),
            ApprovalId: reader.IsDBNull(9) ? null : reader.GetString(9),
            DeliveryMode: reader.IsDBNull(10)
                ? null
                : ChannelHubValidation.ParseEnum<DeliveryMode>(reader.GetString(10)),
            ExternalMessageId: reader.IsDBNull(11) ? null : reader.GetString(11),
            Summary: reader.IsDBNull(12) ? null : reader.GetString(12),
            MetadataJson: reader.IsDBNull(13) ? null : reader.GetString(13));
    }
}
