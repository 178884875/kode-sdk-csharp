using FluentAssertions;
using KodaClaw.Contracts;
using KodaClaw.ControlPlane;
using Xunit;

namespace KodaClaw.UnitTests.ControlPlane;

public sealed class SqliteInboxRepositoryTests
{
    [Fact]
    public async Task Repository_should_create_database_and_round_trip_inbox_item()
    {
        using var tempRoot = new TempWorkspaceRoot();
        var repository = CreateRepository(tempRoot.Path);
        var createdAt = new DateTimeOffset(2026, 3, 18, 12, 0, 0, TimeSpan.Zero);
        var item = new InboxItem(
            Id: "inbox-001",
            Kind: InboxItemKind.Approval,
            Status: InboxItemStatus.Open,
            Title: "Review outbound reply",
            Summary: "A channel reply is waiting for approval.",
            Source: "channel.telegram",
            CreatedAt: createdAt,
            UpdatedAt: createdAt,
            RequiresAction: true,
            Route: "/approvals/approval-001",
            SessionId: "session-main",
            CorrelationId: "corr-001",
            ApprovalId: "approval-001",
            PayloadJson: """{"channel":"telegram"}""");

        await repository.UpsertAsync(item);

        var reloaded = await repository.GetByIdAsync(item.Id);
        var listed = await repository.ListAsync(new InboxQuery(Status: InboxItemStatus.Open, Limit: 10));
        var databasePath = Path.Combine(
            tempRoot.Path,
            KodaClawWorkspaceLayout.ConfigDirectory,
            KodaClawWorkspaceLayout.ControlPlaneDatabaseFile);

        reloaded.Should().Be(item);
        listed.Should().ContainSingle().Which.Should().Be(item);
        File.Exists(databasePath).Should().BeTrue();
    }

    [Fact]
    public async Task Repository_should_filter_items_and_persist_status_transitions()
    {
        using var tempRoot = new TempWorkspaceRoot();
        var repository = CreateRepository(tempRoot.Path);
        var baseTime = new DateTimeOffset(2026, 3, 18, 13, 0, 0, TimeSpan.Zero);

        await repository.UpsertAsync(new InboxItem(
            Id: "inbox-open",
            Kind: InboxItemKind.Alert,
            Status: InboxItemStatus.Open,
            Title: "Automation failed",
            Summary: "Daily job needs attention.",
            Source: "automation.scheduler",
            CreatedAt: baseTime,
            UpdatedAt: baseTime,
            RequiresAction: true,
            SessionId: "session-automation"));
        await repository.UpsertAsync(new InboxItem(
            Id: "inbox-ack",
            Kind: InboxItemKind.Information,
            Status: InboxItemStatus.Acknowledged,
            Title: "Plugin installed",
            Summary: "The browser plugin is now available.",
            Source: "plugin.manager",
            CreatedAt: baseTime.AddMinutes(1),
            UpdatedAt: baseTime.AddMinutes(1),
            RequiresAction: false,
            SessionId: "session-main"));

        var updated = await repository.UpdateStatusAsync(
            "inbox-open",
            InboxItemStatus.Resolved,
            updatedAt: baseTime.AddMinutes(5));

        var actionItems = await repository.ListAsync(new InboxQuery(RequiresAction: true, Limit: 10));
        var resolvedItems = await repository.ListAsync(new InboxQuery(Status: InboxItemStatus.Resolved, Limit: 10));

        updated.Should().BeTrue();
        actionItems.Select(item => item.Id).Should().Equal("inbox-open");
        resolvedItems.Should().ContainSingle();
        resolvedItems[0].Id.Should().Be("inbox-open");
        resolvedItems[0].ResolvedAt.Should().Be(baseTime.AddMinutes(5));
    }

    [Fact]
    public async Task Repository_should_reject_items_without_identity_or_title()
    {
        using var tempRoot = new TempWorkspaceRoot();
        var repository = CreateRepository(tempRoot.Path);
        var item = new InboxItem(
            Id: "",
            Kind: InboxItemKind.Information,
            Status: InboxItemStatus.Open,
            Title: "",
            Summary: "missing identity",
            Source: "tests",
            CreatedAt: new DateTimeOffset(2026, 3, 18, 14, 0, 0, TimeSpan.Zero),
            UpdatedAt: new DateTimeOffset(2026, 3, 18, 14, 0, 0, TimeSpan.Zero));

        var action = () => repository.UpsertAsync(item);

        await action.Should().ThrowAsync<ArgumentException>();
    }

    private static SqliteInboxRepository CreateRepository(string rootPath)
    {
        return new SqliteInboxRepository(new FakeWorkspaceService(rootPath));
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
            return Task.FromResult(CreateSnapshot());
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
        public Task<bool> TryCommitWorkspaceAsync(string message, CancellationToken cancellationToken = default) => Task.FromResult(false);


        private WorkspaceSnapshot CreateSnapshot(bool initialized = false)
        {
            return new WorkspaceSnapshot(
                RootPath,
                KodaClawWorkspaceLayout.CurrentWorkspaceVersion,
                initialized,
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
                "kodaclaw-inbox-unit",
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
