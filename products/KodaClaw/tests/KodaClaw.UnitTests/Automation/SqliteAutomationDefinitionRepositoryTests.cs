using FluentAssertions;
using KodaClaw.Automation;
using KodaClaw.Contracts;
using Xunit;

namespace KodaClaw.UnitTests.Automation;

public sealed class SqliteAutomationDefinitionRepositoryTests
{
    [Fact]
    public async Task List_should_start_empty()
    {
        using var workspace = new TempWorkspaceRoot();
        var repository = CreateRepository(workspace.Path);

        var list = await repository.ListAsync();

        list.Should().BeEmpty();
    }

    [Fact]
    public async Task Upsert_should_round_trip_definition()
    {
        using var workspace = new TempWorkspaceRoot();
        var repository = CreateRepository(workspace.Path);
        var expected = new AutomationDefinition(
            Id: "auto-weekly-001",
            Title: "Weekly Digest",
            Prompt: "Summarize the workspace changes.",
            Source: AutomationDefinitionSource.Heartbeat,
            SourcePath: "workspace/HEARTBEAT.md",
            CronExpression: "30 9 * * 1,3,5",
            Enabled: true,
            InputPaths: new[] { "docs/roadmap.md", "docs/status.md" },
            ModelId: null,
            NotificationChannels: null,
            NotifyMode: AutomationNotifyMode.None,
            CreatedAt: new DateTimeOffset(2026, 3, 18, 9, 0, 0, TimeSpan.Zero),
            UpdatedAt: new DateTimeOffset(2026, 3, 18, 9, 10, 0, TimeSpan.Zero),
            LastRunAt: new DateTimeOffset(2026, 3, 18, 1, 0, 0, TimeSpan.Zero),
            NextRunAt: new DateTimeOffset(2026, 3, 20, 1, 30, 0, TimeSpan.Zero),
            LastRunStatus: AutomationRunStatus.Succeeded,
            LastError: null);

        await repository.UpsertAsync(expected);
        var actual = await repository.GetByIdAsync(expected.Id);
        var databasePath = Path.Combine(
            workspace.Path,
            KodaClawWorkspaceLayout.ConfigDirectory,
            KodaClawWorkspaceLayout.ControlPlaneDatabaseFile);

        actual.Should().NotBeNull();
        actual!.Id.Should().Be(expected.Id);
        actual.Title.Should().Be(expected.Title);
        actual.Prompt.Should().Be(expected.Prompt);
        actual.Source.Should().Be(expected.Source);
        actual.SourcePath.Should().Be(expected.SourcePath);
        actual.Enabled.Should().Be(expected.Enabled);
        actual.CronExpression.Should().Be(expected.CronExpression);
        actual.InputPaths.Should().Equal(expected.InputPaths!);
        actual.CreatedAt.Should().Be(expected.CreatedAt);
        actual.UpdatedAt.Should().Be(expected.UpdatedAt);
        actual.LastRunAt.Should().Be(expected.LastRunAt);
        actual.NextRunAt.Should().Be(expected.NextRunAt);
        actual.LastRunStatus.Should().Be(expected.LastRunStatus);
        actual.LastError.Should().BeNull();
        File.Exists(databasePath).Should().BeTrue();
    }

    [Fact]
    public async Task List_should_filter_by_enabled_and_source()
    {
        using var workspace = new TempWorkspaceRoot();
        var repository = CreateRepository(workspace.Path);
        var now = new DateTimeOffset(2026, 3, 18, 10, 0, 0, TimeSpan.Zero);

        await repository.UpsertAsync(BuildDefinition("auto-001", AutomationDefinitionSource.Heartbeat, enabled: true, now));
        await repository.UpsertAsync(BuildDefinition("auto-002", AutomationDefinitionSource.Heartbeat, enabled: false, now.AddMinutes(1)));
        await repository.UpsertAsync(BuildDefinition("auto-003", AutomationDefinitionSource.Manual, enabled: true, now.AddMinutes(2)));

        var filtered = await repository.ListAsync(new AutomationDefinitionQuery(
            Enabled: true,
            Source: AutomationDefinitionSource.Heartbeat,
            Limit: 20));

        filtered.Should().HaveCount(1);
        filtered[0].Id.Should().Be("auto-001");
    }

    private static SqliteAutomationDefinitionRepository CreateRepository(string rootPath)
    {
        return new SqliteAutomationDefinitionRepository(new FakeWorkspaceService(rootPath));
    }

    private static AutomationDefinition BuildDefinition(
        string id,
        AutomationDefinitionSource source,
        bool enabled,
        DateTimeOffset now)
    {
        return new AutomationDefinition(
            Id: id,
            Title: $"Automation {id}",
            Prompt: "Run scheduled workspace check.",
            Source: source,
            SourcePath: source == AutomationDefinitionSource.Heartbeat ? "workspace/HEARTBEAT.md" : null,
            CronExpression: "0 9 * * *",
            Enabled: enabled,
            InputPaths: new[] { "docs/notes.md" },
            ModelId: null,
            NotificationChannels: null,
            NotifyMode: AutomationNotifyMode.None,
            CreatedAt: now,
            UpdatedAt: now,
            LastRunAt: null,
            NextRunAt: null,
            LastRunStatus: null,
            LastError: null);
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

        public IReadOnlyList<string> GetSkillsPaths() => [];

        public Task<WorkspaceMcpConfig> ReadMcpConfigAsync(CancellationToken cancellationToken = default)
            => Task.FromResult(new WorkspaceMcpConfig());

        public Task SaveMcpConfigAsync(WorkspaceMcpConfig config, CancellationToken cancellationToken = default)
            => Task.CompletedTask;

        public Task<GatewayConfig> ReadGatewayConfigAsync(CancellationToken cancellationToken = default)
            => Task.FromResult(new GatewayConfig());

        public Task SaveGatewayConfigAsync(GatewayConfig config, CancellationToken cancellationToken = default)
            => Task.CompletedTask;

        public Task<bool> TryCommitWorkspaceAsync(string message, CancellationToken cancellationToken = default) => Task.FromResult(false);


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
                "kodaclaw-automation-definition-unit",
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
