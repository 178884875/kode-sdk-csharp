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
            Schedule: new AutomationSchedule(
                Kind: AutomationScheduleKind.Weekly,
                Interval: null,
                LocalTime: "09:30",
                DaysOfWeek: new[] { AutomationScheduleDay.Monday, AutomationScheduleDay.Wednesday, AutomationScheduleDay.Friday }),
            Enabled: true,
            InputPaths: new[] { "docs/roadmap.md", "docs/status.md" },
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
        actual.Schedule.Should().NotBeNull();
        actual.Schedule.Kind.Should().Be(expected.Schedule.Kind);
        actual.Schedule.Interval.Should().Be(expected.Schedule.Interval);
        actual.Schedule.LocalTime.Should().Be(expected.Schedule.LocalTime);
        actual.Schedule.DaysOfWeek.Should().Equal(expected.Schedule.DaysOfWeek!);
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

    [Fact]
    public async Task Upsert_should_reject_hourly_schedule_without_interval()
    {
        using var workspace = new TempWorkspaceRoot();
        var repository = CreateRepository(workspace.Path);
        var definition = BuildDefinition(
            id: "auto-invalid-hourly",
            source: AutomationDefinitionSource.Manual,
            enabled: true,
            now: new DateTimeOffset(2026, 3, 18, 10, 0, 0, TimeSpan.Zero)) with
        {
            Schedule = new AutomationSchedule(
                Kind: AutomationScheduleKind.Hourly,
                Interval: 0,
                LocalTime: null,
                DaysOfWeek: null),
        };

        var action = () => repository.UpsertAsync(definition);

        await action.Should().ThrowAsync<ArgumentException>().Where(ex => ex.ParamName == "Schedule");
    }

    [Fact]
    public async Task Upsert_should_reject_daily_schedule_without_valid_local_time()
    {
        using var workspace = new TempWorkspaceRoot();
        var repository = CreateRepository(workspace.Path);
        var definition = BuildDefinition(
            id: "auto-invalid-daily",
            source: AutomationDefinitionSource.Manual,
            enabled: true,
            now: new DateTimeOffset(2026, 3, 18, 10, 0, 0, TimeSpan.Zero)) with
        {
            Schedule = new AutomationSchedule(
                Kind: AutomationScheduleKind.Daily,
                Interval: null,
                LocalTime: "9:30",
                DaysOfWeek: null),
        };

        var action = () => repository.UpsertAsync(definition);

        await action.Should().ThrowAsync<ArgumentException>().Where(ex => ex.ParamName == "Schedule");
    }

    [Fact]
    public async Task Upsert_should_reject_weekly_schedule_without_days()
    {
        using var workspace = new TempWorkspaceRoot();
        var repository = CreateRepository(workspace.Path);
        var definition = BuildDefinition(
            id: "auto-invalid-weekly",
            source: AutomationDefinitionSource.Manual,
            enabled: true,
            now: new DateTimeOffset(2026, 3, 18, 10, 0, 0, TimeSpan.Zero)) with
        {
            Schedule = new AutomationSchedule(
                Kind: AutomationScheduleKind.Weekly,
                Interval: null,
                LocalTime: "09:30",
                DaysOfWeek: Array.Empty<AutomationScheduleDay>()),
        };

        var action = () => repository.UpsertAsync(definition);

        await action.Should().ThrowAsync<ArgumentException>().Where(ex => ex.ParamName == "Schedule");
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
            Schedule: new AutomationSchedule(
                Kind: AutomationScheduleKind.Daily,
                Interval: null,
                LocalTime: "09:00",
                DaysOfWeek: null),
            Enabled: enabled,
            InputPaths: new[] { "docs/notes.md" },
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
