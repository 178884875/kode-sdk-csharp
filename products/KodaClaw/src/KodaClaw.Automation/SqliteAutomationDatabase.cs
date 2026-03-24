using KodaClaw.Contracts;
using Microsoft.Data.Sqlite;

namespace KodaClaw.Automation;

internal sealed class SqliteAutomationDatabase
{
    private readonly SemaphoreSlim _initializationLock = new(1, 1);
    private readonly IWorkspaceService _workspaceService;
    private string? _databasePath;
    private bool _initialized;

    public SqliteAutomationDatabase(IWorkspaceService workspaceService)
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
                CREATE TABLE IF NOT EXISTS automation_definitions (
                    id TEXT NOT NULL PRIMARY KEY,
                    title TEXT NOT NULL,
                    prompt TEXT NOT NULL,
                    source TEXT NOT NULL,
                    source_path TEXT NULL,
                    schedule_kind TEXT NOT NULL,
                    schedule_interval INTEGER NULL,
                    schedule_local_time TEXT NULL,
                    schedule_days_of_week TEXT NULL,
                    enabled INTEGER NOT NULL,
                    input_paths TEXT NULL,
                    model_id TEXT NULL,
                    notification_channels TEXT NULL,
                    notify_mode INTEGER NOT NULL DEFAULT 0,
                    created_at TEXT NOT NULL,
                    updated_at TEXT NOT NULL,
                    last_run_at TEXT NULL,
                    next_run_at TEXT NULL,
                    last_run_status TEXT NULL,
                    last_error TEXT NULL
                );

                CREATE INDEX IF NOT EXISTS ix_automation_definitions_enabled_updated
                    ON automation_definitions(enabled, updated_at DESC);

                CREATE INDEX IF NOT EXISTS ix_automation_definitions_source_updated
                    ON automation_definitions(source, updated_at DESC);

                CREATE TABLE IF NOT EXISTS automation_runs (
                    run_id TEXT NOT NULL PRIMARY KEY,
                    automation_id TEXT NOT NULL,
                    status TEXT NOT NULL,
                    trigger TEXT NOT NULL,
                    attempt INTEGER NOT NULL,
                    session_id TEXT NULL,
                    started_at TEXT NOT NULL,
                    completed_at TEXT NULL,
                    summary TEXT NULL,
                    error_message TEXT NULL
                );

                CREATE INDEX IF NOT EXISTS ix_automation_runs_automation_started
                    ON automation_runs(automation_id, started_at DESC);

                CREATE INDEX IF NOT EXISTS ix_automation_runs_status_started
                    ON automation_runs(status, started_at DESC);
                """;
            await command.ExecuteNonQueryAsync(cancellationToken);

            // Migration: add model_id column to existing databases (introduced in KC-3901).
            await using var migrateModelId = connection.CreateCommand();
            migrateModelId.CommandText = "ALTER TABLE automation_definitions ADD COLUMN model_id TEXT NULL;";
            try
            {
                await migrateModelId.ExecuteNonQueryAsync(cancellationToken);
            }
            catch (SqliteException ex) when (ex.Message.Contains("duplicate column name", StringComparison.OrdinalIgnoreCase))
            {
                // Column already exists — idempotent migration.
            }

            // Migration: add notification_channels column (KC-4501).
            await using var migrateNotificationChannels = connection.CreateCommand();
            migrateNotificationChannels.CommandText = "ALTER TABLE automation_definitions ADD COLUMN notification_channels TEXT NULL;";
            try
            {
                await migrateNotificationChannels.ExecuteNonQueryAsync(cancellationToken);
            }
            catch (SqliteException ex) when (ex.Message.Contains("duplicate column name", StringComparison.OrdinalIgnoreCase))
            {
                // Column already exists — idempotent migration.
            }

            // Migration: add notify_mode column (KC-4501).
            await using var migrateNotifyMode = connection.CreateCommand();
            migrateNotifyMode.CommandText = "ALTER TABLE automation_definitions ADD COLUMN notify_mode INTEGER NOT NULL DEFAULT 0;";
            try
            {
                await migrateNotifyMode.ExecuteNonQueryAsync(cancellationToken);
            }
            catch (SqliteException ex) when (ex.Message.Contains("duplicate column name", StringComparison.OrdinalIgnoreCase))
            {
                // Column already exists — idempotent migration.
            }

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
