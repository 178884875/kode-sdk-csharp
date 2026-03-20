using System.Text;
using System.Text.Json;
using KodaClaw.Contracts;
using Microsoft.Data.Sqlite;

namespace KodaClaw.ChannelHub;

public sealed class SqliteThreadBindingRepository : IThreadBindingRepository
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    private readonly SqliteChannelHubDatabase _database;

    public SqliteThreadBindingRepository(IWorkspaceService workspaceService)
    {
        _database = new SqliteChannelHubDatabase(
            workspaceService ?? throw new ArgumentNullException(nameof(workspaceService)));
    }

    public async Task UpsertAsync(ThreadBinding binding, CancellationToken cancellationToken = default)
    {
        ChannelHubValidation.ValidateBinding(binding);

        await using var connection = await _database.OpenConnectionAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText =
            """
            INSERT INTO thread_bindings (
                id,
                connector_kind,
                account_id,
                external_thread_id,
                thread_type,
                session_id,
                session_kind,
                channel_identity_json,
                policy_id,
                delivery_rule_id,
                created_at,
                updated_at,
                last_inbound_at,
                last_outbound_at,
                last_message_preview
            )
            VALUES (
                $id,
                $connectorKind,
                $accountId,
                $externalThreadId,
                $threadType,
                $sessionId,
                $sessionKind,
                $channelIdentityJson,
                $policyId,
                $deliveryRuleId,
                $createdAt,
                $updatedAt,
                $lastInboundAt,
                $lastOutboundAt,
                $lastMessagePreview
            )
            ON CONFLICT(id) DO UPDATE SET
                connector_kind = excluded.connector_kind,
                account_id = excluded.account_id,
                external_thread_id = excluded.external_thread_id,
                thread_type = excluded.thread_type,
                session_id = excluded.session_id,
                session_kind = excluded.session_kind,
                channel_identity_json = excluded.channel_identity_json,
                policy_id = excluded.policy_id,
                delivery_rule_id = excluded.delivery_rule_id,
                created_at = excluded.created_at,
                updated_at = excluded.updated_at,
                last_inbound_at = excluded.last_inbound_at,
                last_outbound_at = excluded.last_outbound_at,
                last_message_preview = excluded.last_message_preview;
            """;

        BindBinding(command, binding);
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    public async Task<ThreadBinding?> GetByIdAsync(string id, CancellationToken cancellationToken = default)
    {
        ChannelHubValidation.ValidateId(id, nameof(id));

        await using var connection = await _database.OpenConnectionAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText =
            """
            SELECT
                id,
                connector_kind,
                account_id,
                external_thread_id,
                thread_type,
                session_id,
                session_kind,
                channel_identity_json,
                policy_id,
                delivery_rule_id,
                created_at,
                updated_at,
                last_inbound_at,
                last_outbound_at,
                last_message_preview
            FROM thread_bindings
            WHERE id = $id
            LIMIT 1;
            """;
        command.Parameters.AddWithValue("$id", id);

        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        if (!await reader.ReadAsync(cancellationToken))
        {
            return null;
        }

        return MapBinding(reader);
    }

    public async Task<ThreadBinding?> GetByExternalThreadAsync(
        ChannelConnectorKind connectorKind,
        string accountId,
        string externalThreadId,
        CancellationToken cancellationToken = default)
    {
        ChannelHubValidation.ValidateId(accountId, nameof(accountId));
        ChannelHubValidation.ValidateId(externalThreadId, nameof(externalThreadId));

        await using var connection = await _database.OpenConnectionAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText =
            """
            SELECT
                id,
                connector_kind,
                account_id,
                external_thread_id,
                thread_type,
                session_id,
                session_kind,
                channel_identity_json,
                policy_id,
                delivery_rule_id,
                created_at,
                updated_at,
                last_inbound_at,
                last_outbound_at,
                last_message_preview
            FROM thread_bindings
            WHERE connector_kind = $connectorKind
              AND account_id = $accountId
              AND external_thread_id = $externalThreadId
            LIMIT 1;
            """;
        command.Parameters.AddWithValue("$connectorKind", connectorKind.ToString());
        command.Parameters.AddWithValue("$accountId", accountId);
        command.Parameters.AddWithValue("$externalThreadId", externalThreadId);

        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        if (!await reader.ReadAsync(cancellationToken))
        {
            return null;
        }

        return MapBinding(reader);
    }

    public async Task<IReadOnlyList<ThreadBinding>> ListAsync(
        ChannelQuery? query = null,
        CancellationToken cancellationToken = default)
    {
        query ??= new ChannelQuery();

        await using var connection = await _database.OpenConnectionAsync(cancellationToken);
        await using var command = connection.CreateCommand();

        var sql = new StringBuilder(
            """
            SELECT
                id,
                connector_kind,
                account_id,
                external_thread_id,
                thread_type,
                session_id,
                session_kind,
                channel_identity_json,
                policy_id,
                delivery_rule_id,
                created_at,
                updated_at,
                last_inbound_at,
                last_outbound_at,
                last_message_preview
            FROM thread_bindings
            """);

        var filters = new List<string>();
        if (query.ConnectorKind is { } connectorKind)
        {
            filters.Add("connector_kind = $connectorKind");
            command.Parameters.AddWithValue("$connectorKind", connectorKind.ToString());
        }

        if (!string.IsNullOrWhiteSpace(query.AccountId))
        {
            filters.Add("account_id = $accountId");
            command.Parameters.AddWithValue("$accountId", query.AccountId.Trim());
        }

        if (query.ThreadType is { } threadType)
        {
            filters.Add("thread_type = $threadType");
            command.Parameters.AddWithValue("$threadType", threadType.ToString());
        }

        if (query.SessionKind is { } sessionKind)
        {
            filters.Add("session_kind = $sessionKind");
            command.Parameters.AddWithValue("$sessionKind", sessionKind.ToString());
        }

        if (!string.IsNullOrWhiteSpace(query.SessionId))
        {
            filters.Add("session_id = $sessionId");
            command.Parameters.AddWithValue("$sessionId", query.SessionId.Trim());
        }

        if (filters.Count > 0)
        {
            sql.Append(" WHERE ");
            sql.AppendJoin(" AND ", filters);
        }

        sql.Append(" ORDER BY updated_at DESC, created_at DESC LIMIT $limit;");
        command.Parameters.AddWithValue("$limit", ChannelHubValidation.NormalizeLimit(query.Limit));
        command.CommandText = sql.ToString();

        var bindings = new List<ThreadBinding>();
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            bindings.Add(MapBinding(reader));
        }

        return bindings;
    }

    public async Task<bool> DeleteAsync(string id, CancellationToken cancellationToken = default)
    {
        ChannelHubValidation.ValidateId(id, nameof(id));

        await using var connection = await _database.OpenConnectionAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText =
            """
            DELETE FROM thread_bindings
            WHERE id = $id;
            """;
        command.Parameters.AddWithValue("$id", id);

        var rows = await command.ExecuteNonQueryAsync(cancellationToken);
        return rows > 0;
    }

    private static void BindBinding(SqliteCommand command, ThreadBinding binding)
    {
        command.Parameters.AddWithValue("$id", binding.Id);
        command.Parameters.AddWithValue("$connectorKind", binding.ConnectorKind.ToString());
        command.Parameters.AddWithValue("$accountId", binding.AccountId);
        command.Parameters.AddWithValue("$externalThreadId", binding.ExternalThreadId);
        command.Parameters.AddWithValue("$threadType", binding.ThreadType.ToString());
        command.Parameters.AddWithValue("$sessionId", binding.SessionId);
        command.Parameters.AddWithValue("$sessionKind", binding.SessionKind.ToString());
        command.Parameters.AddWithValue("$channelIdentityJson", JsonSerializer.Serialize(binding.ChannelIdentity, JsonOptions));
        command.Parameters.AddWithValue("$policyId", binding.PolicyId);
        command.Parameters.AddWithValue("$deliveryRuleId", binding.DeliveryRuleId);
        command.Parameters.AddWithValue("$createdAt", ChannelHubValidation.FormatTimestamp(binding.CreatedAt));
        command.Parameters.AddWithValue("$updatedAt", ChannelHubValidation.FormatTimestamp(binding.UpdatedAt));
        command.Parameters.AddWithValue("$lastInboundAt", (object?)ChannelHubValidation.FormatTimestampOrNull(binding.LastInboundAt) ?? DBNull.Value);
        command.Parameters.AddWithValue("$lastOutboundAt", (object?)ChannelHubValidation.FormatTimestampOrNull(binding.LastOutboundAt) ?? DBNull.Value);
        command.Parameters.AddWithValue("$lastMessagePreview", (object?)ChannelHubValidation.NormalizeNullableText(binding.LastMessagePreview) ?? DBNull.Value);
    }

    private static ThreadBinding MapBinding(SqliteDataReader reader)
    {
        var id = reader.GetString(0);
        var channelIdentity = JsonSerializer.Deserialize<ChannelIdentity>(reader.GetString(7), JsonOptions)
            ?? throw new InvalidOperationException($"Channel identity payload is invalid for binding '{id}'.");

        return new ThreadBinding(
            Id: id,
            ConnectorKind: ChannelHubValidation.ParseEnum<ChannelConnectorKind>(reader.GetString(1)),
            AccountId: reader.GetString(2),
            ExternalThreadId: reader.GetString(3),
            ThreadType: ChannelHubValidation.ParseEnum<ChannelThreadType>(reader.GetString(4)),
            SessionId: reader.GetString(5),
            SessionKind: ChannelHubValidation.ParseEnum<SessionKind>(reader.GetString(6)),
            ChannelIdentity: channelIdentity,
            PolicyId: reader.GetString(8),
            DeliveryRuleId: reader.GetString(9),
            CreatedAt: ChannelHubValidation.ParseTimestamp(reader.GetString(10)),
            UpdatedAt: ChannelHubValidation.ParseTimestamp(reader.GetString(11)),
            LastInboundAt: reader.IsDBNull(12) ? null : ChannelHubValidation.ParseTimestamp(reader.GetString(12)),
            LastOutboundAt: reader.IsDBNull(13) ? null : ChannelHubValidation.ParseTimestamp(reader.GetString(13)),
            LastMessagePreview: reader.IsDBNull(14) ? null : reader.GetString(14));
    }
}
