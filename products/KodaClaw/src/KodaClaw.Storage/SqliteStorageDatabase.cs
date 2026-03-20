using KodaClaw.Contracts;
using Microsoft.Data.Sqlite;

namespace KodaClaw.Storage;

internal sealed class SqliteStorageDatabase
{
    private readonly SemaphoreSlim _initializationLock = new(1, 1);
    private readonly IWorkspaceService _workspaceService;
    private string? _databasePath;
    private bool _initialized;

    public SqliteStorageDatabase(IWorkspaceService workspaceService)
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
                CREATE TABLE IF NOT EXISTS canvas_artifacts (
                    id TEXT NOT NULL PRIMARY KEY,
                    title TEXT NOT NULL,
                    kind TEXT NOT NULL,
                    summary TEXT NOT NULL,
                    source TEXT NOT NULL,
                    route TEXT NULL,
                    entry_path TEXT NOT NULL,
                    asset_directory TEXT NOT NULL,
                    session_id TEXT NULL,
                    correlation_id TEXT NULL,
                    created_at TEXT NOT NULL,
                    updated_at TEXT NOT NULL,
                    metadata_json TEXT NULL
                );

                CREATE INDEX IF NOT EXISTS ix_canvas_artifacts_kind_updated
                    ON canvas_artifacts(kind, updated_at DESC, created_at DESC);

                CREATE INDEX IF NOT EXISTS ix_canvas_artifacts_source_updated
                    ON canvas_artifacts(source, updated_at DESC, created_at DESC);

                CREATE INDEX IF NOT EXISTS ix_canvas_artifacts_session_updated
                    ON canvas_artifacts(session_id, updated_at DESC, created_at DESC);
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
