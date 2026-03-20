using System.Collections.Generic;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using FluentAssertions;
using KodaClaw.Contracts;
using Microsoft.Extensions.Configuration;
using Xunit;

namespace KodaClaw.IntegrationTests.Gateway;

public sealed class ChannelApiIntegrationTests
{
    private const string GatewayToken = "test-token";

    [Fact]
    public async Task Channel_accounts_should_require_token()
    {
        using var workspace = new TempWorkspaceRoot();
        await using var hosted = await StartGatewayAsync(workspace.Path);

        var response = await hosted.Client.GetAsync("/api/channels/accounts");

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task Generic_webhook_flow_should_create_thread_binding_and_audit()
    {
        using var workspace = new TempWorkspaceRoot();
        await using var hosted = await StartGatewayAsync(workspace.Path);
        hosted.Client.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", GatewayToken);

        var accountRequest = new UpsertChannelAccountRequest(
            Id: "webhook-main",
            ConnectorKind: ChannelConnectorKind.GenericWebhook,
            DisplayName: "Generic Webhook",
            ConfigurationJson: """
                {
                  "sharedSecret": "hook-secret",
                  "defaultThreadType": "Group"
                }
                """);

        var createAccountResponse = await hosted.Client.PostAsJsonAsync("/api/channels/accounts", accountRequest);
        createAccountResponse.StatusCode.Should().Be(HttpStatusCode.Created);

        using var webhookRequest = new HttpRequestMessage(
            HttpMethod.Post,
            "/api/channels/webhook/webhook-main/events")
        {
            Content = JsonContent.Create(new
            {
                eventType = "message.received",
                eventId = "event-001",
                externalThreadId = "group-thread-001",
                threadType = "group",
                occurredAt = "2026-03-19T09:00:00Z",
                messageId = "message-001",
                text = "incident opened",
                sender = new
                {
                    id = "sender-001",
                    displayName = "Ops Bridge",
                },
            }),
        };
        webhookRequest.Headers.Authorization = new AuthenticationHeaderValue("Bearer", GatewayToken);
        webhookRequest.Headers.Add("X-KodaClaw-Webhook-Secret", "hook-secret");

        var webhookResponse = await hosted.Client.SendAsync(webhookRequest);
        webhookResponse.StatusCode.Should().Be(HttpStatusCode.OK);
        var detail = await webhookResponse.Content.ReadFromJsonAsync<ChannelThreadDetail>();
        detail.Should().NotBeNull();
        detail!.Account.Id.Should().Be("webhook-main");
        detail.Binding.ThreadType.Should().Be(ChannelThreadType.Group);
        detail.Binding.SessionKind.Should().Be(SessionKind.ChannelGroup);
        detail.DeliveryRule.Mode.Should().Be(DeliveryMode.RequireApproval);
        detail.RecentAudit.Should().ContainSingle();
        detail.RecentAudit[0].EventType.Should().Be("message.received");

        var accountsResponse = await hosted.Client.GetAsync("/api/channels/accounts?connectorKind=GenericWebhook");
        accountsResponse.StatusCode.Should().Be(HttpStatusCode.OK);
        var accounts = await accountsResponse.Content.ReadFromJsonAsync<List<ChannelAccount>>();
        accounts.Should().NotBeNull();
        accounts!.Should().ContainSingle(item => item.Id == "webhook-main");

        var threadsResponse = await hosted.Client.GetAsync("/api/channels/threads?connectorKind=GenericWebhook");
        threadsResponse.StatusCode.Should().Be(HttpStatusCode.OK);
        var threads = await threadsResponse.Content.ReadFromJsonAsync<ChannelsQueryResponse>();
        threads.Should().NotBeNull();
        threads!.Items.Should().ContainSingle();
        threads.Items[0].BindingId.Should().Be(detail.Binding.Id);
        threads.Items[0].DisplayTitle.Should().Be("Ops Bridge");
        threads.Items[0].DeliveryMode.Should().Be(DeliveryMode.RequireApproval);

        var auditResponse = await hosted.Client.GetAsync($"/api/channels/threads/{detail.Binding.Id}/audit?limit=10");
        auditResponse.StatusCode.Should().Be(HttpStatusCode.OK);
        var auditItems = await auditResponse.Content.ReadFromJsonAsync<List<ChannelAuditEntry>>();
        auditItems.Should().NotBeNull();
        auditItems!.Should().ContainSingle();
        auditItems![0].BindingId.Should().Be(detail.Binding.Id);
        auditItems[0].EventType.Should().Be("message.received");
        auditItems[0].Summary.Should().Be("incident opened");
    }

    [Fact]
    public async Task Generic_webhook_rejection_should_surface_diagnostics_with_same_correlation_id()
    {
        using var workspace = new TempWorkspaceRoot();
        await using var hosted = await StartGatewayAsync(workspace.Path);
        hosted.Client.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", GatewayToken);

        var accountRequest = new UpsertChannelAccountRequest(
            Id: "webhook-reject",
            ConnectorKind: ChannelConnectorKind.GenericWebhook,
            DisplayName: "Rejecting Webhook",
            ConfigurationJson: """{"sharedSecret":"expected-secret"}""");
        var createAccountResponse = await hosted.Client.PostAsJsonAsync("/api/channels/accounts", accountRequest);
        createAccountResponse.StatusCode.Should().Be(HttpStatusCode.Created);

        using var webhookRequest = new HttpRequestMessage(
            HttpMethod.Post,
            "/api/channels/webhook/webhook-reject/events")
        {
            Content = JsonContent.Create(new
            {
                eventType = "message.received",
                externalThreadId = "thread-reject-001",
                text = "should reject",
            }),
        };
        webhookRequest.Headers.Authorization = new AuthenticationHeaderValue("Bearer", GatewayToken);
        webhookRequest.Headers.Add("X-KodaClaw-Webhook-Secret", "wrong-secret");
        webhookRequest.Headers.Add("X-KodaClaw-Correlation-Id", "corr-channel-reject-001");

        var webhookResponse = await hosted.Client.SendAsync(webhookRequest);
        webhookResponse.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        var error = await webhookResponse.Content.ReadFromJsonAsync<ErrorResponse>();
        error.Should().NotBeNull();
        error!.Code.Should().Be("channel.webhook_secret_mismatch");

        var diagnosticsResponse = await hosted.Client.GetAsync(
            "/api/diagnostics/recent?correlationId=corr-channel-reject-001");
        diagnosticsResponse.StatusCode.Should().Be(HttpStatusCode.OK);
        var diagnostics = await diagnosticsResponse.Content.ReadFromJsonAsync<DiagnosticsQueryResponse>();
        diagnostics.Should().NotBeNull();
        diagnostics!.Events.Should().Contain(item =>
            item.EventType == "gateway.channels.webhook_rejected" &&
            item.CorrelationId == "corr-channel-reject-001");
    }

    private static Task<HostedGateway> StartGatewayAsync(string workspaceRoot)
    {
        return HostedGateway.StartAsync(
            gatewayToken: GatewayToken,
            workspaceSnapshot: GatewayAuthIntegrationTests.CreateSnapshot(
                requiresBootstrap: false,
                rootPath: workspaceRoot),
            configureConfiguration: configuration =>
            {
                configuration.AddInMemoryCollection(new Dictionary<string, string?>
                {
                    ["KODACLAW_WORKSPACE_ROOT"] = workspaceRoot,
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
                "kodaclaw-channel-api",
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
