using System.Text;
using KodaClaw.Contracts;
using Microsoft.Data.Sqlite;

namespace KodaClaw.ChannelHub;

public sealed class SqliteChannelAccountRepository : IChannelAccountRepository
{
    private readonly SqliteChannelHubDatabase _database;

    public SqliteChannelAccountRepository(IWorkspaceService workspaceService)
    {
        _database = new SqliteChannelHubDatabase(
            workspaceService ?? throw new ArgumentNullException(nameof(workspaceService)));
    }

    public async Task UpsertAsync(ChannelAccount account, CancellationToken cancellationToken = default)
    {
        ChannelHubValidation.ValidateAccount(account);

        await using var connection = await _database.OpenConnectionAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText =
            """
            INSERT INTO channel_accounts (
                id,
                connector_kind,
                display_name,
                state,
                external_account_id,
                credential_reference,
                description,
                configuration_json,
                inbound_enabled,
                created_at,
                updated_at,
                last_connected_at,
                last_disconnected_at,
                last_error
            )
            VALUES (
                $id,
                $connectorKind,
                $displayName,
                $state,
                $externalAccountId,
                $credentialReference,
                $description,
                $configurationJson,
                $inboundEnabled,
                $createdAt,
                $updatedAt,
                $lastConnectedAt,
                $lastDisconnectedAt,
                $lastError
            )
            ON CONFLICT(id) DO UPDATE SET
                connector_kind = excluded.connector_kind,
                display_name = excluded.display_name,
                state = excluded.state,
                external_account_id = excluded.external_account_id,
                credential_reference = excluded.credential_reference,
                description = excluded.description,
                configuration_json = excluded.configuration_json,
                inbound_enabled = excluded.inbound_enabled,
                created_at = excluded.created_at,
                updated_at = excluded.updated_at,
                last_connected_at = excluded.last_connected_at,
                last_disconnected_at = excluded.last_disconnected_at,
                last_error = excluded.last_error;
            """;

        BindAccount(command, account);
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    public async Task<ChannelAccount?> GetByIdAsync(string id, CancellationToken cancellationToken = default)
    {
        ChannelHubValidation.ValidateId(id, nameof(id));

        await using var connection = await _database.OpenConnectionAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText =
            """
            SELECT
                id,
                connector_kind,
                display_name,
                state,
                external_account_id,
                credential_reference,
                description,
                configuration_json,
                inbound_enabled,
                created_at,
                updated_at,
                last_connected_at,
                last_disconnected_at,
                last_error
            FROM channel_accounts
            WHERE id = $id
            LIMIT 1;
            """;
        command.Parameters.AddWithValue("$id", id);

        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        if (!await reader.ReadAsync(cancellationToken))
        {
            return null;
        }

        return MapAccount(reader);
    }

    public async Task<IReadOnlyList<ChannelAccount>> ListAsync(
        ChannelAccountQuery? query = null,
        CancellationToken cancellationToken = default)
    {
        query ??= new ChannelAccountQuery();

        await using var connection = await _database.OpenConnectionAsync(cancellationToken);
        await using var command = connection.CreateCommand();

        var sql = new StringBuilder(
            """
            SELECT
                id,
                connector_kind,
                display_name,
                state,
                external_account_id,
                credential_reference,
                description,
                configuration_json,
                inbound_enabled,
                created_at,
                updated_at,
                last_connected_at,
                last_disconnected_at,
                last_error
            FROM channel_accounts
            """);

        var filters = new List<string>();
        if (query.ConnectorKind is { } connectorKind)
        {
            filters.Add("connector_kind = $connectorKind");
            command.Parameters.AddWithValue("$connectorKind", connectorKind.ToString());
        }

        if (query.State is { } state)
        {
            filters.Add("state = $state");
            command.Parameters.AddWithValue("$state", state.ToString());
        }

        if (filters.Count > 0)
        {
            sql.Append(" WHERE ");
            sql.AppendJoin(" AND ", filters);
        }

        sql.Append(" ORDER BY updated_at DESC, created_at DESC LIMIT $limit OFFSET $offset;");
        command.Parameters.AddWithValue("$limit", ChannelHubValidation.NormalizeLimit(query.Limit));
        command.Parameters.AddWithValue("$offset", Math.Max(0, query.Offset));
        command.CommandText = sql.ToString();

        var accounts = new List<ChannelAccount>();
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            accounts.Add(MapAccount(reader));
        }

        return accounts;
    }

    public async Task<bool> DeleteAsync(string id, CancellationToken cancellationToken = default)
    {
        ChannelHubValidation.ValidateId(id, nameof(id));

        await using var connection = await _database.OpenConnectionAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText =
            """
            DELETE FROM channel_accounts
            WHERE id = $id;
            """;
        command.Parameters.AddWithValue("$id", id);

        var rows = await command.ExecuteNonQueryAsync(cancellationToken);
        return rows > 0;
    }

    private static void BindAccount(SqliteCommand command, ChannelAccount account)
    {
        command.Parameters.AddWithValue("$id", account.Id);
        command.Parameters.AddWithValue("$connectorKind", account.ConnectorKind.ToString());
        command.Parameters.AddWithValue("$displayName", account.DisplayName);
        command.Parameters.AddWithValue("$state", account.State.ToString());
        command.Parameters.AddWithValue("$externalAccountId", (object?)ChannelHubValidation.NormalizeNullableText(account.ExternalAccountId) ?? DBNull.Value);
        command.Parameters.AddWithValue("$credentialReference", (object?)ChannelHubValidation.NormalizeNullableText(account.CredentialReference) ?? DBNull.Value);
        command.Parameters.AddWithValue("$description", (object?)ChannelHubValidation.NormalizeNullableText(account.Description) ?? DBNull.Value);
        command.Parameters.AddWithValue("$configurationJson", (object?)ChannelHubValidation.NormalizeNullableText(account.ConfigurationJson) ?? DBNull.Value);
        command.Parameters.AddWithValue("$inboundEnabled", account.InboundEnabled ? 1 : 0);
        command.Parameters.AddWithValue("$createdAt", ChannelHubValidation.FormatTimestamp(account.CreatedAt));
        command.Parameters.AddWithValue("$updatedAt", ChannelHubValidation.FormatTimestamp(account.UpdatedAt));
        command.Parameters.AddWithValue("$lastConnectedAt", (object?)ChannelHubValidation.FormatTimestampOrNull(account.LastConnectedAt) ?? DBNull.Value);
        command.Parameters.AddWithValue("$lastDisconnectedAt", (object?)ChannelHubValidation.FormatTimestampOrNull(account.LastDisconnectedAt) ?? DBNull.Value);
        command.Parameters.AddWithValue("$lastError", (object?)ChannelHubValidation.NormalizeNullableText(account.LastError) ?? DBNull.Value);
    }

    private static ChannelAccount MapAccount(SqliteDataReader reader)
    {
        return new ChannelAccount(
            Id: reader.GetString(0),
            ConnectorKind: ChannelHubValidation.ParseEnum<ChannelConnectorKind>(reader.GetString(1)),
            DisplayName: reader.GetString(2),
            State: ChannelHubValidation.ParseEnum<ChannelAccountState>(reader.GetString(3)),
            CreatedAt: ChannelHubValidation.ParseTimestamp(reader.GetString(9)),
            UpdatedAt: ChannelHubValidation.ParseTimestamp(reader.GetString(10)),
            ExternalAccountId: reader.IsDBNull(4) ? null : reader.GetString(4),
            CredentialReference: reader.IsDBNull(5) ? null : reader.GetString(5),
            Description: reader.IsDBNull(6) ? null : reader.GetString(6),
            ConfigurationJson: reader.IsDBNull(7) ? null : reader.GetString(7),
            InboundEnabled: reader.GetInt64(8) != 0,
            LastConnectedAt: reader.IsDBNull(11) ? null : ChannelHubValidation.ParseTimestamp(reader.GetString(11)),
            LastDisconnectedAt: reader.IsDBNull(12) ? null : ChannelHubValidation.ParseTimestamp(reader.GetString(12)),
            LastError: reader.IsDBNull(13) ? null : reader.GetString(13));
    }
}
