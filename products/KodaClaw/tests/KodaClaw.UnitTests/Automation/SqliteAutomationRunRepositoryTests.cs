using FluentAssertions;
using KodaClaw.Automation;
using KodaClaw.Contracts;
using Xunit;

namespace KodaClaw.UnitTests.Automation;

public sealed class SqliteAutomationRunRepositoryTests
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
    public async Task Add_and_get_should_round_trip_run_record()
    {
        using var workspace = new TempWorkspaceRoot();
        var repository = CreateRepository(workspace.Path);
        var expected = BuildRunRecord(
            runId: "run-001",
            automationId: "daily-inbox-digest",
            status: AutomationRunStatus.Running,
            startedAt: new DateTimeOffset(2026, 3, 18, 9, 0, 0, TimeSpan.Zero)) with
        {
            SessionId = "auto-20260318090000-daily-abcd1234",
        };

        await repository.AddAsync(expected);
        var actual = await repository.GetByIdAsync(expected.RunId);

        actual.Should().Be(expected);
    }

    [Fact]
    public async Task Update_should_persist_terminal_fields()
    {
        using var workspace = new TempWorkspaceRoot();
        var repository = CreateRepository(workspace.Path);
        var startedAt = new DateTimeOffset(2026, 3, 18, 9, 0, 0, TimeSpan.Zero);
        var runRecord = BuildRunRecord(
            runId: "run-002",
            automationId: "daily-inbox-digest",
            status: AutomationRunStatus.Running,
            startedAt: startedAt);

        await repository.AddAsync(runRecord);

        var updated = runRecord with
        {
            Status = AutomationRunStatus.Failed,
            CompletedAt = startedAt.AddMinutes(3),
            Summary = "Digest generation failed.",
            ErrorMessage = "Model timeout.",
        };

        var didUpdate = await repository.UpdateAsync(updated);
        var reloaded = await repository.GetByIdAsync(updated.RunId);

        didUpdate.Should().BeTrue();
        reloaded.Should().Be(updated);
    }

    [Fact]
    public async Task List_should_filter_by_automation_id_and_status()
    {
        using var workspace = new TempWorkspaceRoot();
        var repository = CreateRepository(workspace.Path);
        var startedAt = new DateTimeOffset(2026, 3, 18, 9, 0, 0, TimeSpan.Zero);

        await repository.AddAsync(BuildRunRecord("run-a", "daily-inbox-digest", AutomationRunStatus.Succeeded, startedAt));
        await repository.AddAsync(BuildRunRecord("run-b", "daily-inbox-digest", AutomationRunStatus.Failed, startedAt.AddMinutes(1)));
        await repository.AddAsync(BuildRunRecord("run-c", "weekly-review", AutomationRunStatus.Failed, startedAt.AddMinutes(2)));

        var filtered = await repository.ListAsync(new AutomationRunQuery(
            AutomationId: "daily-inbox-digest",
            Status: AutomationRunStatus.Failed,
            Limit: 10));

        filtered.Should().HaveCount(1);
        filtered[0].RunId.Should().Be("run-b");
    }

    [Fact]
    public async Task Add_should_reject_invalid_attempt()
    {
        using var workspace = new TempWorkspaceRoot();
        var repository = CreateRepository(workspace.Path);
        var invalid = BuildRunRecord(
            runId: "run-invalid",
            automationId: "daily-inbox-digest",
            status: AutomationRunStatus.Queued,
            startedAt: new DateTimeOffset(2026, 3, 18, 9, 0, 0, TimeSpan.Zero)) with
        {
            Attempt = 0,
        };

        var action = () => repository.AddAsync(invalid);

        await action.Should().ThrowAsync<ArgumentException>().Where(ex => ex.ParamName == "Attempt");
    }

    private static SqliteAutomationRunRepository CreateRepository(string rootPath)
    {
        return new SqliteAutomationRunRepository(new FakeWorkspaceService(rootPath));
    }

    private static AutomationRunRecord BuildRunRecord(
        string runId,
        string automationId,
        AutomationRunStatus status,
        DateTimeOffset startedAt)
    {
        return new AutomationRunRecord(
            RunId: runId,
            AutomationId: automationId,
            Status: status,
            Trigger: "heartbeat",
            Attempt: 1,
            SessionId: null,
            StartedAt: startedAt,
            CompletedAt: null,
            Summary: null,
            ErrorMessage: null);
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
                "kodaclaw-automation-run-unit",
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
