using FluentAssertions;
using KodaClaw.Contracts;
using KodaClaw.PluginHost.Diagnostics;
using Microsoft.Data.Sqlite;
using Xunit;

namespace KodaClaw.UnitTests.PluginHost;

public sealed class SqlitePluginLogRepositoryTests
{
    [Fact]
    public async Task List_should_start_empty()
    {
        using var workspace = new TempWorkspaceRoot();
        var repository = CreateRepository(workspace.Path);

        var entries = await repository.ListAsync("plugin.todo");

        entries.Should().BeEmpty();
    }

    [Fact]
    public async Task Append_and_list_should_return_newest_first_and_respect_limit()
    {
        using var workspace = new TempWorkspaceRoot();
        var repository = CreateRepository(workspace.Path);
        var baseTime = new DateTimeOffset(2026, 3, 19, 2, 0, 0, TimeSpan.Zero);

        await repository.AppendAsync(new PluginLogEntry(
            EntryId: "log-1",
            PluginId: "plugin.todo",
            Level: "INFO",
            Source: "stdio",
            Message: "first",
            Timestamp: baseTime));
        await repository.AppendAsync(new PluginLogEntry(
            EntryId: "log-2",
            PluginId: "plugin.todo",
            Level: "WARN",
            Source: "stdio",
            Message: "second",
            Timestamp: baseTime.AddSeconds(1)));
        await repository.AppendAsync(new PluginLogEntry(
            EntryId: "log-3",
            PluginId: "plugin.todo",
            Level: "ERROR",
            Source: "stdio",
            Message: "third",
            Timestamp: baseTime.AddSeconds(2)));

        var entries = await repository.ListAsync("plugin.todo", limit: 2);

        entries.Should().HaveCount(2);
        entries[0].EntryId.Should().Be("log-3");
        entries[1].EntryId.Should().Be("log-2");
    }

    [Fact]
    public async Task List_should_not_return_entries_for_other_plugins()
    {
        using var workspace = new TempWorkspaceRoot();
        var repository = CreateRepository(workspace.Path);
        var now = new DateTimeOffset(2026, 3, 19, 2, 30, 0, TimeSpan.Zero);

        await repository.AppendAsync(new PluginLogEntry(
            EntryId: "todo-1",
            PluginId: "plugin.todo",
            Level: "INFO",
            Source: "stdio",
            Message: "todo",
            Timestamp: now));
        await repository.AppendAsync(new PluginLogEntry(
            EntryId: "other-1",
            PluginId: "plugin.other",
            Level: "INFO",
            Source: "stdio",
            Message: "other",
            Timestamp: now.AddSeconds(1)));

        var entries = await repository.ListAsync("plugin.todo");

        entries.Should().ContainSingle();
        entries[0].EntryId.Should().Be("todo-1");
        entries[0].PluginId.Should().Be("plugin.todo");
    }

    [Fact]
    public async Task Append_should_round_trip_payload_json()
    {
        using var workspace = new TempWorkspaceRoot();
        var repository = CreateRepository(workspace.Path);
        var now = new DateTimeOffset(2026, 3, 19, 3, 0, 0, TimeSpan.Zero);

        await repository.AppendAsync(new PluginLogEntry(
            EntryId: "payload-1",
            PluginId: "plugin.todo",
            Level: "INFO",
            Source: "tool",
            Message: "hello",
            Timestamp: now,
            PayloadJson: """{"foo":"bar","n":1}"""));

        var entries = await repository.ListAsync("plugin.todo");

        entries.Should().ContainSingle();
        entries[0].PayloadJson.Should().Be("""{"foo":"bar","n":1}""");
    }

    [Fact]
    public async Task Repository_should_initialize_plugin_logs_table_in_control_plane_db()
    {
        using var workspace = new TempWorkspaceRoot();
        var repository = CreateRepository(workspace.Path);
        await repository.AppendAsync(new PluginLogEntry(
            EntryId: "table-init-1",
            PluginId: "plugin.table-init",
            Level: "INFO",
            Source: "stdio",
            Message: "init",
            Timestamp: DateTimeOffset.UtcNow));

        var databasePath = Path.Combine(
            workspace.Path,
            KodaClawWorkspaceLayout.ConfigDirectory,
            KodaClawWorkspaceLayout.ControlPlaneDatabaseFile);
        File.Exists(databasePath).Should().BeTrue();

        var builder = new SqliteConnectionStringBuilder
        {
            DataSource = databasePath,
        };
        await using var connection = new SqliteConnection(builder.ToString());
        await connection.OpenAsync();

        await using var command = connection.CreateCommand();
        command.CommandText =
            """
            SELECT name
            FROM sqlite_master
            WHERE type = 'table' AND name = 'plugin_logs'
            LIMIT 1;
            """;

        var tableName = await command.ExecuteScalarAsync();
        tableName.Should().Be("plugin_logs");
    }

    [Fact]
    public async Task List_should_validate_plugin_id()
    {
        using var workspace = new TempWorkspaceRoot();
        var repository = CreateRepository(workspace.Path);

        var act = async () => await repository.ListAsync(" ");

        await act.Should().ThrowAsync<ArgumentException>()
            .WithMessage("*Plugin id is required*");
    }

    [Fact]
    public async Task Append_should_validate_entry_fields()
    {
        using var workspace = new TempWorkspaceRoot();
        var repository = CreateRepository(workspace.Path);

        var act = async () => await repository.AppendAsync(new PluginLogEntry(
            EntryId: "",
            PluginId: "plugin.todo",
            Level: "INFO",
            Source: "stdio",
            Message: "msg",
            Timestamp: DateTimeOffset.UtcNow));

        await act.Should().ThrowAsync<ArgumentException>()
            .WithMessage("*Entry id is required*");
    }

    private static SqlitePluginLogRepository CreateRepository(string rootPath)
    {
        return new SqlitePluginLogRepository(new FakeWorkspaceService(rootPath));
    }

    private sealed class FakeWorkspaceService : IWorkspaceService
    {
        public FakeWorkspaceService(string rootPath)
        {
            RootPath = rootPath;
        }

        public string RootPath { get; }

        public Task<WorkspaceSnapshot> GetSnapshotAsync(CancellationToken cancellationToken = default)
        {
            return Task.FromResult(CreateSnapshot(initialized: true));
        }

        public Task<WorkspaceSnapshot> EnsureInitializedAsync(CancellationToken cancellationToken = default)
        {
            Directory.CreateDirectory(RootPath);
            Directory.CreateDirectory(Path.Combine(RootPath, KodaClawWorkspaceLayout.ConfigDirectory));
            return Task.FromResult(CreateSnapshot(initialized: true));
        }

        public Task<WorkspaceAppConfig> LoadAppConfigAsync(CancellationToken cancellationToken = default)
        {
            return Task.FromResult(new WorkspaceAppConfig());
        }

        public Task SaveAppConfigAsync(WorkspaceAppConfig appConfig, CancellationToken cancellationToken = default)
        {
            return Task.CompletedTask;
        }

        public string GetSessionDirectory(string sessionId)
        {
            return Path.Combine(RootPath, KodaClawWorkspaceLayout.SessionsDirectory, sessionId);
        }

        private WorkspaceSnapshot CreateSnapshot(bool initialized)
        {
            return new WorkspaceSnapshot(
                RootPath: RootPath,
                WorkspaceVersion: KodaClawWorkspaceLayout.CurrentWorkspaceVersion,
                WorkspaceInitialized: initialized,
                RequiresBootstrap: !initialized,
                ActiveMainSessionId: null,
                DeviceId: null);
        }
    }

    private sealed class TempWorkspaceRoot : IDisposable
    {
        public TempWorkspaceRoot()
        {
            Path = System.IO.Path.Combine(
                System.IO.Path.GetTempPath(),
                "kodaclaw-plugin-log-unit",
                Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(Path);
        }

        public string Path { get; }

        public void Dispose()
        {
            if (Directory.Exists(Path))
            {
                Directory.Delete(Path, recursive: true);
            }
        }
    }
}

