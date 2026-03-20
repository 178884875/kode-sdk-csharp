using KodaClaw.Contracts;
using Microsoft.Data.Sqlite;

namespace KodaClaw.PluginHost.Diagnostics;

internal sealed class SqlitePluginLogDatabase
{
    private readonly SemaphoreSlim _initializationLock = new(1, 1);
    private readonly IWorkspaceService _workspaceService;
    private string? _databasePath;
    private bool _initialized;

    public SqlitePluginLogDatabase(IWorkspaceService workspaceService)
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
                CREATE TABLE IF NOT EXISTS plugin_logs (
                    entry_id TEXT NOT NULL PRIMARY KEY,
                    plugin_id TEXT NOT NULL,
                    level TEXT NOT NULL,
                    source TEXT NOT NULL,
                    message TEXT NOT NULL,
                    timestamp TEXT NOT NULL,
                    payload_json TEXT NULL
                );

                CREATE INDEX IF NOT EXISTS ix_plugin_logs_plugin_timestamp
                    ON plugin_logs(plugin_id, timestamp DESC);

                CREATE INDEX IF NOT EXISTS ix_plugin_logs_timestamp
                    ON plugin_logs(timestamp DESC);
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

