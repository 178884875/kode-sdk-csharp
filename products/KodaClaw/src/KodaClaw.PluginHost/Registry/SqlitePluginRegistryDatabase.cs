using KodaClaw.Contracts;
using Microsoft.Data.Sqlite;

namespace KodaClaw.PluginHost.Registry;

internal sealed class SqlitePluginRegistryDatabase
{
    private readonly SemaphoreSlim _initializationLock = new(1, 1);
    private readonly IWorkspaceService _workspaceService;
    private string? _databasePath;
    private bool _initialized;

    public SqlitePluginRegistryDatabase(IWorkspaceService workspaceService)
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
                CREATE TABLE IF NOT EXISTS plugins (
                    id TEXT NOT NULL PRIMARY KEY,
                    manifest_json TEXT NOT NULL,
                    types_csv TEXT NOT NULL,
                    install_source TEXT NOT NULL,
                    root_path TEXT NOT NULL,
                    trust_state TEXT NOT NULL,
                    enabled INTEGER NOT NULL,
                    runtime_state TEXT NOT NULL,
                    discovered_at TEXT NOT NULL,
                    installed_at TEXT NOT NULL,
                    updated_at TEXT NOT NULL,
                    last_started_at TEXT NULL,
                    last_stopped_at TEXT NULL,
                    last_health_at TEXT NULL,
                    restart_count INTEGER NOT NULL,
                    last_error TEXT NULL,
                    trust_evidence_json TEXT NULL
                );

                CREATE INDEX IF NOT EXISTS ix_plugins_state_updated
                    ON plugins(trust_state, enabled, runtime_state, updated_at DESC);

                CREATE INDEX IF NOT EXISTS ix_plugins_installed_updated
                    ON plugins(installed_at DESC, updated_at DESC);

                CREATE INDEX IF NOT EXISTS ix_plugins_types_updated
                    ON plugins(types_csv, updated_at DESC);
                """;
            await command.ExecuteNonQueryAsync(cancellationToken);
            await EnsureColumnAsync(
                connection,
                tableName: "plugins",
                columnName: "trust_evidence_json",
                columnDefinition: "TEXT NULL",
                cancellationToken);

            _databasePath = databasePath;
            _initialized = true;
            return databasePath;
        }
        finally
        {
            _initializationLock.Release();
        }
    }

    private static async Task EnsureColumnAsync(
        SqliteConnection connection,
        string tableName,
        string columnName,
        string columnDefinition,
        CancellationToken cancellationToken)
    {
        await using var existsCommand = connection.CreateCommand();
        existsCommand.CommandText = $"PRAGMA table_info({tableName});";

        await using var reader = await existsCommand.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            if (string.Equals(reader.GetString(1), columnName, StringComparison.Ordinal))
            {
                return;
            }
        }

        await reader.DisposeAsync();

        await using var alterCommand = connection.CreateCommand();
        alterCommand.CommandText = $"ALTER TABLE {tableName} ADD COLUMN {columnName} {columnDefinition};";
        await alterCommand.ExecuteNonQueryAsync(cancellationToken);
    }
}
