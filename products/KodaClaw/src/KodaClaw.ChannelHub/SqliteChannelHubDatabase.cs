using KodaClaw.Contracts;
using Microsoft.Data.Sqlite;

namespace KodaClaw.ChannelHub;

internal sealed class SqliteChannelHubDatabase
{
    private readonly SemaphoreSlim _initializationLock = new(1, 1);
    private readonly IWorkspaceService _workspaceService;
    private string? _databasePath;
    private bool _initialized;

    public SqliteChannelHubDatabase(IWorkspaceService workspaceService)
    {
        _workspaceService = workspaceService ?? throw new ArgumentNullException(nameof(workspaceService));
    }

    public async Task<SqliteConnection> OpenConnectionAsync(CancellationToken cancellationToken)
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
                CREATE TABLE IF NOT EXISTS channel_accounts (
                    id TEXT NOT NULL PRIMARY KEY,
                    connector_kind TEXT NOT NULL,
                    display_name TEXT NOT NULL,
                    state TEXT NOT NULL,
                    external_account_id TEXT NULL,
                    credential_reference TEXT NULL,
                    description TEXT NULL,
                    configuration_json TEXT NULL,
                    inbound_enabled INTEGER NOT NULL,
                    created_at TEXT NOT NULL,
                    updated_at TEXT NOT NULL,
                    last_connected_at TEXT NULL,
                    last_disconnected_at TEXT NULL,
                    last_error TEXT NULL
                );

                CREATE INDEX IF NOT EXISTS ix_channel_accounts_connector_state_updated
                    ON channel_accounts(connector_kind, state, updated_at DESC, created_at DESC);

                CREATE TABLE IF NOT EXISTS thread_bindings (
                    id TEXT NOT NULL PRIMARY KEY,
                    connector_kind TEXT NOT NULL,
                    account_id TEXT NOT NULL,
                    external_thread_id TEXT NOT NULL,
                    thread_type TEXT NOT NULL,
                    session_id TEXT NOT NULL,
                    session_kind TEXT NOT NULL,
                    channel_identity_json TEXT NOT NULL,
                    policy_id TEXT NOT NULL,
                    delivery_rule_id TEXT NOT NULL,
                    created_at TEXT NOT NULL,
                    updated_at TEXT NOT NULL,
                    last_inbound_at TEXT NULL,
                    last_outbound_at TEXT NULL,
                    last_message_preview TEXT NULL
                );

                CREATE UNIQUE INDEX IF NOT EXISTS ux_thread_bindings_connector_account_thread
                    ON thread_bindings(connector_kind, account_id, external_thread_id);

                CREATE INDEX IF NOT EXISTS ix_thread_bindings_account_updated
                    ON thread_bindings(account_id, updated_at DESC, created_at DESC);

                CREATE INDEX IF NOT EXISTS ix_thread_bindings_session_updated
                    ON thread_bindings(session_id, updated_at DESC, created_at DESC);

                CREATE INDEX IF NOT EXISTS ix_thread_bindings_thread_type_updated
                    ON thread_bindings(thread_type, updated_at DESC, created_at DESC);
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
}
