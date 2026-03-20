using System.Globalization;
using KodaClaw.Contracts;
using Microsoft.Data.Sqlite;

namespace KodaClaw.ControlPlane;

public sealed class SqliteSettingsRepository : ISettingsRepository
{
    private const string SettingsRowId = "app-settings";
    private const string QuietHoursTimeFormat = "HH:mm";

    private readonly SemaphoreSlim _initializationLock = new(1, 1);
    private readonly IWorkspaceService _workspaceService;
    private string? _databasePath;
    private bool _initialized;

    public SqliteSettingsRepository(IWorkspaceService workspaceService)
    {
        _workspaceService = workspaceService ?? throw new ArgumentNullException(nameof(workspaceService));
    }

    public async Task<KodaClawSettings> GetAsync(CancellationToken cancellationToken = default)
    {
        await using var connection = await OpenConnectionAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText =
            """
            SELECT
                default_landing_route,
                theme,
                require_approval,
                notifications_enabled,
                quiet_hours_enabled,
                quiet_hours_start,
                quiet_hours_end,
                updated_at,
                automations_enabled
            FROM app_settings
            WHERE id = $id
            LIMIT 1;
            """;
        command.Parameters.AddWithValue("$id", SettingsRowId);

        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        if (!await reader.ReadAsync(cancellationToken))
        {
            return KodaClawSettings.Default;
        }

        return MapSettings(reader);
    }

    public async Task SaveAsync(KodaClawSettings settings, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(settings);
        Validate(settings);

        await using var connection = await OpenConnectionAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText =
            """
            INSERT INTO app_settings (
                id,
                default_landing_route,
                theme,
                require_approval,
                notifications_enabled,
                quiet_hours_enabled,
                quiet_hours_start,
                quiet_hours_end,
                updated_at,
                automations_enabled
            )
            VALUES (
                $id,
                $defaultLandingRoute,
                $theme,
                $requireApproval,
                $notificationsEnabled,
                $quietHoursEnabled,
                $quietHoursStart,
                $quietHoursEnd,
                $updatedAt,
                $automationsEnabled
            )
            ON CONFLICT(id) DO UPDATE SET
                default_landing_route = excluded.default_landing_route,
                theme = excluded.theme,
                require_approval = excluded.require_approval,
                notifications_enabled = excluded.notifications_enabled,
                quiet_hours_enabled = excluded.quiet_hours_enabled,
                quiet_hours_start = excluded.quiet_hours_start,
                quiet_hours_end = excluded.quiet_hours_end,
                updated_at = excluded.updated_at,
                automations_enabled = excluded.automations_enabled;
            """;

        BindParameters(command, settings);
        await command.ExecuteNonQueryAsync(cancellationToken);
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
                CREATE TABLE IF NOT EXISTS app_settings (
                    id TEXT NOT NULL PRIMARY KEY,
                    default_landing_route TEXT NOT NULL,
                    theme TEXT NOT NULL,
                    require_approval INTEGER NOT NULL,
                    notifications_enabled INTEGER NOT NULL,
                    quiet_hours_enabled INTEGER NOT NULL,
                    quiet_hours_start TEXT NULL,
                    quiet_hours_end TEXT NULL,
                    updated_at TEXT NOT NULL
                );
                """;
            await command.ExecuteNonQueryAsync(cancellationToken);

            // Migration: add automations_enabled column if not present (introduced in KC-1402).
            await using var migrateCommand = connection.CreateCommand();
            migrateCommand.CommandText =
                "ALTER TABLE app_settings ADD COLUMN automations_enabled INTEGER NOT NULL DEFAULT 0;";
            try
            {
                await migrateCommand.ExecuteNonQueryAsync(cancellationToken);
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

    private static void BindParameters(SqliteCommand command, KodaClawSettings settings)
    {
        command.Parameters.AddWithValue("$id", SettingsRowId);
        command.Parameters.AddWithValue("$defaultLandingRoute", settings.DefaultLandingRoute);
        command.Parameters.AddWithValue("$theme", settings.Theme.ToString());
        command.Parameters.AddWithValue("$requireApproval", settings.RequireApprovalForExternalActions ? 1 : 0);
        command.Parameters.AddWithValue("$notificationsEnabled", settings.NotificationsEnabled ? 1 : 0);
        command.Parameters.AddWithValue("$quietHoursEnabled", settings.QuietHoursEnabled ? 1 : 0);
        command.Parameters.AddWithValue("$quietHoursStart", (object?)NormalizeQuietHours(settings.QuietHoursStartLocalTime) ?? DBNull.Value);
        command.Parameters.AddWithValue("$quietHoursEnd", (object?)NormalizeQuietHours(settings.QuietHoursEndLocalTime) ?? DBNull.Value);
        command.Parameters.AddWithValue("$updatedAt", FormatTimestamp(settings.UpdatedAt));
        command.Parameters.AddWithValue("$automationsEnabled", settings.AutomationsEnabled ? 1 : 0);
    }

    private static KodaClawSettings MapSettings(SqliteDataReader reader)
    {
        return new KodaClawSettings(
            DefaultLandingRoute: reader.GetString(0),
            Theme: ParseEnum<ThemeMode>(reader.GetString(1)),
            RequireApprovalForExternalActions: reader.GetInt64(2) != 0,
            NotificationsEnabled: reader.GetInt64(3) != 0,
            QuietHoursEnabled: reader.GetInt64(4) != 0,
            QuietHoursStartLocalTime: reader.IsDBNull(5) ? null : reader.GetString(5),
            QuietHoursEndLocalTime: reader.IsDBNull(6) ? null : reader.GetString(6),
            UpdatedAt: ParseTimestamp(reader.GetString(7)),
            AutomationsEnabled: reader.GetInt64(8) != 0);
    }

    private static void Validate(KodaClawSettings settings)
    {
        ValidateQuietHours(settings.QuietHoursStartLocalTime, nameof(settings.QuietHoursStartLocalTime));
        ValidateQuietHours(settings.QuietHoursEndLocalTime, nameof(settings.QuietHoursEndLocalTime));
    }

    private static void ValidateQuietHours(string? value, string parameterName)
    {
        if (value is not { Length: > 0 })
        {
            return;
        }

        var normalized = value.Trim();
        if (normalized.Length == 0)
        {
            return;
        }

        if (!TimeOnly.TryParseExact(normalized, QuietHoursTimeFormat, CultureInfo.InvariantCulture, DateTimeStyles.None, out _))
        {
            throw new ArgumentException("Quiet hours must use HH:mm format.", parameterName);
        }
    }

    private static string? NormalizeQuietHours(string? value)
    {
        if (value is null)
        {
            return null;
        }

        var trimmed = value.Trim();
        return trimmed.Length == 0 ? null : trimmed;
    }

    private static string FormatTimestamp(DateTimeOffset value)
    {
        return value.ToString("O", CultureInfo.InvariantCulture);
    }

    private static DateTimeOffset ParseTimestamp(string rawValue)
    {
        return DateTimeOffset.Parse(rawValue, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind);
    }

    private static TEnum ParseEnum<TEnum>(string rawValue)
        where TEnum : struct, Enum
    {
        return Enum.Parse<TEnum>(rawValue, ignoreCase: false);
    }
}
