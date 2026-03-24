using FluentAssertions;
using KodaClaw.Contracts;
using KodaClaw.ControlPlane;
using Xunit;

namespace KodaClaw.UnitTests.ControlPlane;

public sealed class SqliteApprovalRepositoryTests
{
    [Fact]
    public async Task Repository_should_create_database_and_round_trip_approval()
    {
        using var tempRoot = new TempWorkspaceRoot();
        var repository = CreateRepository(tempRoot.Path);
        var requestedAt = new DateTimeOffset(2026, 3, 18, 12, 0, 0, TimeSpan.Zero);
        var approval = new Approval(
            Id: "approval-001",
            Kind: ApprovalKind.OutboundMessage,
            Status: ApprovalStatus.Pending,
            Title: "Approve outbound reply",
            Summary: "Control-plane ready to send.",
            Source: "channel.telegram",
            RequestedAt: requestedAt,
            UpdatedAt: requestedAt,
            SessionId: "session-main",
            CorrelationId: "corr-001",
            InboxItemId: "inbox-001",
            PayloadJson: """{""channel"":""telegram""}""");

        await repository.UpsertAsync(approval);

        var reloaded = await repository.GetByIdAsync(approval.Id);
        var listed = await repository.ListAsync(new ApprovalQuery(Status: ApprovalStatus.Pending, Limit: 10));
        var databasePath = Path.Combine(
            tempRoot.Path,
            KodaClawWorkspaceLayout.ConfigDirectory,
            KodaClawWorkspaceLayout.ControlPlaneDatabaseFile);

        reloaded.Should().Be(approval);
        listed.Should().ContainSingle().Which.Should().Be(approval);
        File.Exists(databasePath).Should().BeTrue();
    }

    [Fact]
    public async Task Repository_should_filter_approvals_and_reject_repeat_transitions()
    {
        using var tempRoot = new TempWorkspaceRoot();
        var repository = CreateRepository(tempRoot.Path);
        var baseTime = new DateTimeOffset(2026, 3, 18, 13, 0, 0, TimeSpan.Zero);

        await repository.UpsertAsync(new Approval(
            Id: "approval-pending",
            Kind: ApprovalKind.OutboundEmail,
            Status: ApprovalStatus.Pending,
            Title: "Approve email",
            Summary: "Customer requested review.",
            Source: "channel.email",
            RequestedAt: baseTime,
            UpdatedAt: baseTime,
            SessionId: "session-main"));
        await repository.UpsertAsync(new Approval(
            Id: "approval-other",
            Kind: ApprovalKind.PluginAuthorization,
            Status: ApprovalStatus.Pending,
            Title: "Install plugin",
            Summary: "Await security approval.",
            Source: "plugin.manager",
            RequestedAt: baseTime.AddMinutes(1),
            UpdatedAt: baseTime.AddMinutes(1),
            SessionId: "session-other"));

        var transitionTime = baseTime.AddMinutes(5);
        var transitioned = await repository.TransitionAsync(
            "approval-pending",
            ApprovalStatus.Approved,
            transitionTime,
            decidedBy: "alice",
            decisionNote: "Looks good.");

        var filtered = await repository.ListAsync(new ApprovalQuery(
            Status: ApprovalStatus.Approved,
            Kind: ApprovalKind.OutboundEmail,
            SessionId: "session-main",
            Limit: 10));

        var reloaded = await repository.GetByIdAsync("approval-pending");
        transitioned.Should().BeTrue();
        filtered.Should().ContainSingle().Which.Id.Should().Be("approval-pending");
        reloaded.Should().NotBeNull();
        reloaded!.Status.Should().Be(ApprovalStatus.Approved);
        reloaded.DecidedAt.Should().Be(transitionTime);
        reloaded.DecidedBy.Should().Be("alice");
        reloaded.DecisionNote.Should().Be("Looks good.");

        var secondTransition = await repository.TransitionAsync(
            "approval-pending",
            ApprovalStatus.Rejected,
            transitionTime.AddMinutes(1),
            decidedBy: "bob",
            decisionNote: "Needs follow-up.");
        secondTransition.Should().BeFalse();
    }

    private static SqliteApprovalRepository CreateRepository(string rootPath)
    {
        return new SqliteApprovalRepository(new FakeWorkspaceService(rootPath));
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
                "kodaclaw-approval-unit",
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
