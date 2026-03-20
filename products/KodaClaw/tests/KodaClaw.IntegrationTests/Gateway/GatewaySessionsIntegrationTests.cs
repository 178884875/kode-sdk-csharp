using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using FluentAssertions;
using KodaClaw.Contracts;
using KodaClaw.Workspace;
using Kode.Agent.Sdk.Core.Abstractions;
using Kode.Agent.Sdk.Core.Types;
using Kode.Agent.Store.Json;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace KodaClaw.IntegrationTests.Gateway;

public sealed class GatewaySessionsIntegrationTests
{
    [Fact]
    public async Task Sessions_list_should_require_token()
    {
        using var workspace = new TempWorkspaceRoot();
        await SeedWorkspaceConfigAsync(workspace.Path, activeMainSessionId: null);
        await using var hosted = await StartRealWorkspaceGatewayAsync(workspace.Path);

        var response = await hosted.Client.GetAsync("/api/sessions");

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task Sessions_list_should_return_status_summary_for_existing_sessions()
    {
        using var workspace = new TempWorkspaceRoot();
        await SeedWorkspaceConfigAsync(workspace.Path, activeMainSessionId: "session-main-001");

        await SeedSessionAsync(
            workspace.Path,
            "session-main-001",
            CreateAgentInfo(
                "session-main-001",
                BreakpointState.AwaitingApproval,
                messageCount: 3,
                lastSfpIndex: 7,
                createdAt: "2026-03-18T10:00:00Z",
                lastBookmarkTimestamp: 1_763_289_700_000),
            messages:
            [
                Message.User("hello"),
                Message.Assistant("world"),
                Message.User("follow-up")
            ],
            toolCalls:
            [
                CreateApprovalRequiredCall("call-approval-001")
            ]);

        await SeedSessionAsync(
            workspace.Path,
            "session-side-001",
            CreateAgentInfo(
                "session-side-001",
                BreakpointState.Ready,
                messageCount: 1,
                lastSfpIndex: 2,
                createdAt: "2026-03-18T09:00:00Z",
                lastBookmarkTimestamp: 1_763_289_600_000),
            messages:
            [
                Message.User("secondary")
            ]);

        await using var hosted = await StartRealWorkspaceGatewayAsync(workspace.Path);
        hosted.Client.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", "test-token");

        var response = await hosted.Client.GetAsync("/api/sessions");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var payload = await response.Content.ReadFromJsonAsync<SessionsQueryResponse>();
        payload.Should().NotBeNull();
        payload!.Sessions.Should().HaveCount(2);

        var main = payload.Sessions.Single(item => item.SessionId == "session-main-001");
        main.SessionKind.Should().Be(SessionKind.Main);
        main.Status.IsActiveMainSession.Should().BeTrue();
        main.Status.BreakpointState.Should().Be("AwaitingApproval");
        main.Status.PendingApprovalCount.Should().Be(1);
        main.Status.MessageCount.Should().Be(3);
        main.CreatedAt.Should().NotBeNull();
        main.LastEventAt.Should().NotBeNull();

        var side = payload.Sessions.Single(item => item.SessionId == "session-side-001");
        side.Status.IsActiveMainSession.Should().BeFalse();
        side.Status.BreakpointState.Should().Be("Ready");
        side.Status.PendingApprovalCount.Should().Be(0);
        side.Status.MessageCount.Should().Be(1);
    }

    [Fact]
    public async Task Sessions_detail_should_return_not_found_when_session_missing()
    {
        using var workspace = new TempWorkspaceRoot();
        await SeedWorkspaceConfigAsync(workspace.Path, activeMainSessionId: "session-main-001");
        await using var hosted = await StartRealWorkspaceGatewayAsync(workspace.Path);
        hosted.Client.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", "test-token");

        var response = await hosted.Client.GetAsync("/api/sessions/missing-session");

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
        var payload = await response.Content.ReadFromJsonAsync<ErrorResponse>();
        payload.Should().NotBeNull();
        payload!.Code.Should().Be("session.not_found");
    }

    [Fact]
    public async Task Sessions_detail_should_return_aggregated_detail_with_pending_approval_summary()
    {
        using var workspace = new TempWorkspaceRoot();
        await SeedWorkspaceConfigAsync(workspace.Path, activeMainSessionId: "session-main-001");

        await SeedSessionAsync(
            workspace.Path,
            "session-main-001",
            CreateAgentInfo(
                "session-main-001",
                BreakpointState.AwaitingApproval,
                messageCount: 3,
                lastSfpIndex: 11,
                createdAt: "2026-03-18T10:00:00Z",
                lastBookmarkTimestamp: 1_763_289_900_000),
            messages:
            [
                Message.User("u1"),
                Message.Assistant("a1"),
                Message.User("u2")
            ],
            toolCalls:
            [
                CreateApprovalRequiredCall("call-approval-001"),
                CreateCompletedCall("call-completed-001")
            ]);

        await using var hosted = await StartRealWorkspaceGatewayAsync(workspace.Path);
        hosted.Client.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", "test-token");

        var response = await hosted.Client.GetAsync("/api/sessions/session-main-001");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var payload = await response.Content.ReadFromJsonAsync<SessionDetail>();
        payload.Should().NotBeNull();
        payload!.SessionId.Should().Be("session-main-001");
        payload.SessionKind.Should().Be(SessionKind.Main);
        payload.Status.IsActiveMainSession.Should().BeTrue();
        payload.Status.BreakpointState.Should().Be("AwaitingApproval");
        payload.Status.PendingApprovalCount.Should().Be(1);
        payload.Status.MessageCount.Should().Be(3);
        payload.UserMessageCount.Should().Be(2);
        payload.AssistantMessageCount.Should().Be(1);
        payload.ToolCallCount.Should().Be(2);
        payload.LastSfpIndex.Should().Be(11);
        payload.PendingApprovalCallIds.Should().ContainSingle().Which.Should().Be("call-approval-001");
        payload.LastEventAt.Should().NotBeNull();
    }

    [Fact]
    public async Task Sessions_list_should_infer_channel_session_kind_from_channel_session_id()
    {
        using var workspace = new TempWorkspaceRoot();
        await SeedWorkspaceConfigAsync(workspace.Path, activeMainSessionId: null);

        await SeedSessionAsync(
            workspace.Path,
            "channel-dm-binding-001",
            CreateAgentInfo(
                "channel-dm-binding-001",
                BreakpointState.Ready,
                messageCount: 2,
                lastSfpIndex: 4,
                createdAt: "2026-03-18T11:00:00Z",
                lastBookmarkTimestamp: 1_763_290_000_000),
            messages:
            [
                Message.User("hello"),
                Message.Assistant("reply"),
            ]);

        await using var hosted = await StartRealWorkspaceGatewayAsync(workspace.Path);
        hosted.Client.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", "test-token");

        var response = await hosted.Client.GetAsync("/api/sessions");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var payload = await response.Content.ReadFromJsonAsync<SessionsQueryResponse>();
        payload.Should().NotBeNull();
        payload!.Sessions.Should().ContainSingle();
        payload.Sessions[0].SessionId.Should().Be("channel-dm-binding-001");
        payload.Sessions[0].SessionKind.Should().Be(SessionKind.ChannelDirectMessage);
        payload.Sessions[0].Status.IsActiveMainSession.Should().BeFalse();
    }

    private static async Task SeedWorkspaceConfigAsync(string workspaceRoot, string? activeMainSessionId)
    {
        var services = new ServiceCollection();
        services.AddKodaClawWorkspace(options => options.RootPath = workspaceRoot);
        using var provider = services.BuildServiceProvider();

        var workspace = provider.GetRequiredService<IWorkspaceService>();
        await workspace.EnsureInitializedAsync();
        await workspace.SaveAppConfigAsync(new WorkspaceAppConfig
        {
            WorkspaceVersion = KodaClawWorkspaceLayout.CurrentWorkspaceVersion,
            BootstrapCompleted = true,
            ActiveMainSessionId = activeMainSessionId
        });
    }

    private static async Task SeedSessionAsync(
        string workspaceRoot,
        string sessionId,
        AgentInfo info,
        IReadOnlyList<Message>? messages = null,
        IReadOnlyList<ToolCallRecord>? toolCalls = null)
    {
        var sessionsRoot = Path.Combine(workspaceRoot, KodaClawWorkspaceLayout.SessionsDirectory);
        var store = new JsonAgentStore(sessionsRoot);

        await store.SaveInfoAsync(sessionId, info);
        await store.SaveMessagesAsync(sessionId, messages ?? []);
        await store.SaveToolCallRecordsAsync(sessionId, toolCalls ?? []);
    }

    private static AgentInfo CreateAgentInfo(
        string sessionId,
        BreakpointState breakpoint,
        int messageCount,
        int lastSfpIndex,
        string createdAt,
        long? lastBookmarkTimestamp)
    {
        return new AgentInfo
        {
            AgentId = sessionId,
            CreatedAt = createdAt,
            MessageCount = messageCount,
            LastSfpIndex = lastSfpIndex,
            Breakpoint = breakpoint,
            LastBookmark = lastBookmarkTimestamp is null
                ? null
                : new Bookmark
                {
                    Seq = 1,
                    Timestamp = lastBookmarkTimestamp.Value
                }
        };
    }

    private static ToolCallRecord CreateApprovalRequiredCall(string callId)
    {
        return new ToolCallRecord
        {
            Id = callId,
            Name = "bash_run",
            Input = new { command = "echo pending" },
            State = ToolCallState.ApprovalRequired,
            Approval = new ToolCallApproval
            {
                Required = true,
                Decision = null,
                DecidedBy = null
            },
            CreatedAt = 1_763_289_800_000,
            UpdatedAt = 1_763_289_800_000
        };
    }

    private static ToolCallRecord CreateCompletedCall(string callId)
    {
        return new ToolCallRecord
        {
            Id = callId,
            Name = "fs_read",
            Input = new { path = "/tmp/demo.txt" },
            State = ToolCallState.Completed,
            Approval = new ToolCallApproval
            {
                Required = false
            },
            CreatedAt = 1_763_289_801_000,
            UpdatedAt = 1_763_289_802_000
        };
    }

    private static Task<HostedGateway> StartRealWorkspaceGatewayAsync(string workspaceRoot)
    {
        return HostedGateway.StartAsync(
            gatewayToken: "test-token",
            workspaceSnapshot: GatewayAuthIntegrationTests.CreateSnapshot(
                requiresBootstrap: false,
                rootPath: workspaceRoot),
            configureConfiguration: configuration =>
            {
                configuration.AddInMemoryCollection(new Dictionary<string, string?>
                {
                    ["KODACLAW_WORKSPACE_ROOT"] = workspaceRoot
                });
            },
            useTestWorkspaceService: false);
    }

    private sealed class TempWorkspaceRoot : IDisposable
    {
        public TempWorkspaceRoot()
        {
            Path = System.IO.Path.Combine(
                System.IO.Path.GetTempPath(),
                "kodaclaw-sessions-api",
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
