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
                    schedule_kind TEXT NULL,
                    schedule_interval INTEGER NULL,
                    schedule_local_time TEXT NULL,
                    schedule_days_of_week TEXT NULL,
                    cron TEXT NULL,
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

            // Migration: add cron column (KC-5503); legacy schedule_* columns are preserved for
            // existing rows but no longer written by new code.
            await using var migrateCron = connection.CreateCommand();
            migrateCron.CommandText = "ALTER TABLE automation_definitions ADD COLUMN cron TEXT NULL;";
            try
            {
                await migrateCron.ExecuteNonQueryAsync(cancellationToken);
            }
            catch (SqliteException ex) when (ex.Message.Contains("duplicate column name", StringComparison.OrdinalIgnoreCase))
            {
                // Column already exists — idempotent migration.
            }

            // Migration: relax schedule_kind NOT NULL → NULL (KC-5503).
            // SQLite does not support ALTER COLUMN, so we use the standard rename-recreate pattern.
            // Idempotency guard: only run when schedule_kind is still NOT NULL in the existing schema.
            await using var pragmaCmd = connection.CreateCommand();
            pragmaCmd.CommandText =
                "SELECT [notnull] FROM pragma_table_info('automation_definitions') WHERE name='schedule_kind';";
            var scheduleKindNotNull = await pragmaCmd.ExecuteScalarAsync(cancellationToken) is long n && n == 1;

            if (scheduleKindNotNull)
            {
                await using var tx = connection.BeginTransaction();
                try
                {
                    static SqliteCommand Cmd(SqliteConnection c, SqliteTransaction t, string sql)
                    {
                        var cmd = c.CreateCommand();
                        cmd.Transaction = t;
                        cmd.CommandText = sql;
                        return cmd;
                    }

                    await using (var c = Cmd(connection, tx,
                        "ALTER TABLE automation_definitions RENAME TO automation_definitions_legacy_5503;"))
                        await c.ExecuteNonQueryAsync(cancellationToken);

                    await using (var c = Cmd(connection, tx,
                        """
                        CREATE TABLE automation_definitions (
                            id TEXT NOT NULL PRIMARY KEY,
                            title TEXT NOT NULL,
                            prompt TEXT NOT NULL,
                            source TEXT NOT NULL,
                            source_path TEXT NULL,
                            schedule_kind TEXT NULL,
                            schedule_interval INTEGER NULL,
                            schedule_local_time TEXT NULL,
                            schedule_days_of_week TEXT NULL,
                            cron TEXT NULL,
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
                        """))
                        await c.ExecuteNonQueryAsync(cancellationToken);

                    // Explicit column list avoids SELECT * column-order mismatch: the legacy
                    // table has 'cron' appended at the end via ALTER TABLE, but the new table
                    // puts 'cron' between schedule_days_of_week and enabled.
                    await using (var c = Cmd(connection, tx,
                        """
                        INSERT INTO automation_definitions (
                            id, title, prompt, source, source_path,
                            schedule_kind, schedule_interval, schedule_local_time, schedule_days_of_week,
                            cron, enabled, input_paths, model_id, notification_channels, notify_mode,
                            created_at, updated_at, last_run_at, next_run_at, last_run_status, last_error
                        )
                        SELECT
                            id, title, prompt, source, source_path,
                            schedule_kind, schedule_interval, schedule_local_time, schedule_days_of_week,
                            cron, enabled, input_paths, model_id, notification_channels, notify_mode,
                            created_at, updated_at, last_run_at, next_run_at, last_run_status, last_error
                        FROM automation_definitions_legacy_5503;
                        """))
                        await c.ExecuteNonQueryAsync(cancellationToken);

                    await using (var c = Cmd(connection, tx,
                        "DROP TABLE automation_definitions_legacy_5503;"))
                        await c.ExecuteNonQueryAsync(cancellationToken);

                    tx.Commit();
                }
                catch
                {
                    tx.Rollback();
                    throw;
                }

                // Recreate indexes dropped by table recreation.
                await using var idxEnabled = connection.CreateCommand();
                idxEnabled.CommandText =
                    "CREATE INDEX IF NOT EXISTS ix_automation_definitions_enabled_updated ON automation_definitions(enabled, updated_at DESC);";
                await idxEnabled.ExecuteNonQueryAsync(cancellationToken);

                await using var idxSource = connection.CreateCommand();
                idxSource.CommandText =
                    "CREATE INDEX IF NOT EXISTS ix_automation_definitions_source_updated ON automation_definitions(source, updated_at DESC);";
                await idxSource.ExecuteNonQueryAsync(cancellationToken);
            }

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
