using System.Collections.Generic;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using FluentAssertions;
using KodaClaw.ChannelHub.Connectors.Telegram;
using KodaClaw.Contracts;
using Kode.Agent.Sdk.Core.Abstractions;
using Kode.Agent.Sdk.Core.Types;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
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

    [Fact]
    public async Task Generic_webhook_with_runtime_should_create_dm_draft_outcome()
    {
        using var workspace = new TempWorkspaceRoot();
        var modelProvider = new StubChannelTurnModelProvider(
            """{"action":"propose_reply","replyText":"Thanks, I have a draft reply ready.","reason":"User asked for a direct response.","confidence":0.93}""");
        await using var hosted = await StartGatewayAsync(workspace.Path, modelProvider, enableRuntime: true);
        hosted.Client.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", GatewayToken);

        var accountRequest = new UpsertChannelAccountRequest(
            Id: "webhook-dm-runtime",
            ConnectorKind: ChannelConnectorKind.GenericWebhook,
            DisplayName: "Runtime DM Webhook",
            ConfigurationJson: """{"sharedSecret":"hook-secret","defaultThreadType":"DirectMessage","defaultDeliveryMode":"DraftApproval"}""");
        var createAccountResponse = await hosted.Client.PostAsJsonAsync("/api/channels/accounts", accountRequest);
        createAccountResponse.StatusCode.Should().Be(HttpStatusCode.Created);

        using var webhookRequest = new HttpRequestMessage(
            HttpMethod.Post,
            "/api/channels/webhook/webhook-dm-runtime/events")
        {
            Content = JsonContent.Create(new
            {
                eventType = "message.received",
                eventId = "event-runtime-dm-001",
                externalThreadId = "dm-thread-001",
                threadType = "directMessage",
                occurredAt = "2026-03-20T03:00:00Z",
                messageId = "message-runtime-dm-001",
                text = "Can you reply on my behalf?",
                sender = new
                {
                    id = "sender-dm-001",
                    displayName = "Alice",
                },
            }),
        };
        webhookRequest.Headers.Authorization = new AuthenticationHeaderValue("Bearer", GatewayToken);
        webhookRequest.Headers.Add("X-KodaClaw-Webhook-Secret", "hook-secret");

        var webhookResponse = await hosted.Client.SendAsync(webhookRequest);
        webhookResponse.StatusCode.Should().Be(HttpStatusCode.OK);

        var detail = await webhookResponse.Content.ReadFromJsonAsync<ChannelThreadDetail>();
        detail.Should().NotBeNull();
        detail!.Binding.ThreadType.Should().Be(ChannelThreadType.DirectMessage);
        detail.PendingApprovalId.Should().NotBeNullOrWhiteSpace();
        detail.HasPendingDraft.Should().BeTrue();
        detail.PolicyEvidence.Should().Contain("pending_approval");
        detail.LastTurnOutcome.Should().NotBeNull();
        detail.LastTurnOutcome!.Kind.Should().Be(ChannelTurnOutcomeKind.DraftCreated);
        detail.LastTurnOutcome.ReplyText.Should().Be("Thanks, I have a draft reply ready.");
        detail.LastTurnOutcome!.ReasonCode.Should().Be("draft_created");
        detail.LastTurnOutcome.HasExplicitMention.Should().BeFalse();

        detail.RecentAudit.Select(item => item.EventType).Should().Contain(new[]
        {
            "message.received",
            "turn.draft_created",
        });

        var threadsResponse = await hosted.Client.GetFromJsonAsync<ChannelsQueryResponse>(
            "/api/channels/threads?connectorKind=GenericWebhook&accountId=webhook-dm-runtime");
        threadsResponse.Should().NotBeNull();
        threadsResponse!.Items.Should().ContainSingle();
        threadsResponse.Items[0].LastTurnOutcome.Should().NotBeNull();
        threadsResponse.Items[0].LastTurnOutcome!.Kind.Should().Be(ChannelTurnOutcomeKind.DraftCreated);
        threadsResponse.Items[0].LastTurnOutcome!.ReasonCode.Should().Be("draft_created");
    }

    [Fact]
    public async Task Generic_webhook_with_runtime_should_block_group_reply_without_explicit_mention()
    {
        using var workspace = new TempWorkspaceRoot();
        var modelProvider = new StubChannelTurnModelProvider(
            """{"action":"propose_reply","replyText":"I can help with that.","reason":"There is a plausible answer.","confidence":0.84}""");
        await using var hosted = await StartGatewayAsync(workspace.Path, modelProvider, enableRuntime: true);
        hosted.Client.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", GatewayToken);

        var accountRequest = new UpsertChannelAccountRequest(
            Id: "webhook-group-runtime",
            ConnectorKind: ChannelConnectorKind.GenericWebhook,
            DisplayName: "Runtime Group Webhook",
            ConfigurationJson: """{"sharedSecret":"hook-secret","defaultThreadType":"Group"}""");
        var createAccountResponse = await hosted.Client.PostAsJsonAsync("/api/channels/accounts", accountRequest);
        createAccountResponse.StatusCode.Should().Be(HttpStatusCode.Created);

        using var webhookRequest = new HttpRequestMessage(
            HttpMethod.Post,
            "/api/channels/webhook/webhook-group-runtime/events")
        {
            Content = JsonContent.Create(new
            {
                eventType = "message.received",
                eventId = "event-runtime-group-001",
                externalThreadId = "group-thread-002",
                threadType = "group",
                occurredAt = "2026-03-20T04:00:00Z",
                messageId = "message-runtime-group-001",
                text = "What should we do next on this incident?",
                sender = new
                {
                    id = "sender-group-001",
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
        detail!.Binding.ThreadType.Should().Be(ChannelThreadType.Group);
        detail.PendingApprovalId.Should().BeNull();
        detail.HasPendingDraft.Should().BeFalse();
        detail.PolicyEvidence.Should().Contain("group_mention_required");
        detail.PolicyEvidence.Should().Contain("blocked_without_mention");
        detail.PolicyEvidence.Should().Contain(item => item.StartsWith("last_no_action|"));
        detail.LastTurnOutcome.Should().NotBeNull();
        detail.LastTurnOutcome!.Kind.Should().Be(ChannelTurnOutcomeKind.NoAction);
        detail.LastTurnOutcome.Summary.Should().Contain("Reply blocked by channel policy");
        detail.LastTurnOutcome!.ReasonCode.Should().Be("policy_blocked_requires_mention");
        detail.LastTurnOutcome.HasExplicitMention.Should().BeFalse();

        detail.RecentAudit.Select(item => item.EventType).Should().Contain(new[]
        {
            "message.received",
            "turn.no_action",
        });
    }

    [Fact]
    public async Task Telegram_account_create_should_auto_start_polling_and_orchestrate_inbound_turn()
    {
        using var workspace = new TempWorkspaceRoot();
        var fakeTelegram = new FakeTelegramApiClient(
        [
            [CreateDirectMessageUpdate(1001, 11, "Please reply on my behalf.")],
        ]);
        var modelProvider = new StubChannelTurnModelProvider(
            """{"action":"propose_reply","replyText":"I drafted a concise reply for approval.","reason":"DM explicitly asked for a response.","confidence":0.91}""");

        await using var hosted = await StartGatewayAsync(
            workspace.Path,
            modelProvider,
            enableRuntime: true,
            configureServices: services =>
            {
                services.AddSingleton<ITelegramApiClient>(fakeTelegram);
            });
        hosted.Client.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", GatewayToken);

        var createAccountResponse = await hosted.Client.PostAsJsonAsync(
            "/api/channels/accounts",
            new UpsertChannelAccountRequest(
                Id: "telegram-runtime",
                ConnectorKind: ChannelConnectorKind.Telegram,
                DisplayName: "Telegram Runtime",
                CredentialReference: "inline:telegram-token-runtime",
                ConfigurationJson: """{"defaultDeliveryMode":"DraftApproval"}"""));
        createAccountResponse.StatusCode.Should().Be(HttpStatusCode.Created);
        var account = await createAccountResponse.Content.ReadFromJsonAsync<ChannelAccount>();
        account.Should().NotBeNull();
        account!.State.Should().Be(ChannelAccountState.Connected);

        ChannelThreadSummary? thread = null;
        ChannelThreadDetail? detail = null;

        await EventuallyAsync(async () =>
        {
            var threads = await hosted.Client.GetFromJsonAsync<ChannelsQueryResponse>(
                "/api/channels/threads?connectorKind=Telegram&accountId=telegram-runtime&limit=20");
            thread = threads?.Items.SingleOrDefault();
            if (thread is null)
            {
                return false;
            }

            detail = await hosted.Client.GetFromJsonAsync<ChannelThreadDetail>(
                $"/api/channels/threads/{thread.BindingId}");
            return thread.LastTurnOutcome is not null
                && detail?.PendingApprovalId is not null
                && detail.LastTurnOutcome is not null;
        }, "Timed out waiting for telegram polling to create a thread outcome.");

        thread.Should().NotBeNull();
        thread!.LastTurnOutcome.Should().NotBeNull();
        thread.LastTurnOutcome!.Kind.Should().Be(ChannelTurnOutcomeKind.DraftCreated);
        thread.LastTurnOutcome!.ReasonCode.Should().Be("draft_created");

        detail.Should().NotBeNull();
        detail!.Binding.SessionKind.Should().Be(SessionKind.ChannelDirectMessage);
        detail.PendingApprovalId.Should().NotBeNullOrWhiteSpace();
        detail.HasPendingDraft.Should().BeTrue();
        detail.LastTurnOutcome.Should().NotBeNull();
        detail.LastTurnOutcome!.Kind.Should().Be(ChannelTurnOutcomeKind.DraftCreated);
        detail.LastTurnOutcome.ReplyText.Should().Be("I drafted a concise reply for approval.");
        detail.LastTurnOutcome!.ReasonCode.Should().Be("draft_created");
        detail.RecentAudit.Select(item => item.EventType).Should().Contain(new[]
        {
            "message.received",
            "turn.draft_created",
        });
        detail.PolicyEvidence.Should().Contain("pending_approval");
    }

    [Fact]
    public async Task Rejecting_pending_channel_delivery_should_surface_policy_evidence_in_thread_detail()
    {
        using var workspace = new TempWorkspaceRoot();
        var modelProvider = new StubChannelTurnModelProvider(
            """{"action":"propose_reply","replyText":"I drafted a reply for review.","reason":"DM asked for a reply.","confidence":0.89}""");
        await using var hosted = await StartGatewayAsync(workspace.Path, modelProvider, enableRuntime: true);
        hosted.Client.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", GatewayToken);

        var createAccountResponse = await hosted.Client.PostAsJsonAsync(
            "/api/channels/accounts",
            new UpsertChannelAccountRequest(
                Id: "webhook-dm-reject",
                ConnectorKind: ChannelConnectorKind.GenericWebhook,
                DisplayName: "Webhook DM Reject",
                ConfigurationJson: """{"sharedSecret":"hook-secret","defaultThreadType":"DirectMessage","defaultDeliveryMode":"DraftApproval"}"""));
        createAccountResponse.StatusCode.Should().Be(HttpStatusCode.Created);

        using var webhookRequest = new HttpRequestMessage(
            HttpMethod.Post,
            "/api/channels/webhook/webhook-dm-reject/events")
        {
            Content = JsonContent.Create(new
            {
                eventType = "message.received",
                eventId = "event-runtime-dm-reject-001",
                externalThreadId = "dm-thread-reject-001",
                threadType = "directMessage",
                occurredAt = "2026-03-20T05:00:00Z",
                messageId = "message-runtime-dm-reject-001",
                text = "Please respond for me.",
                sender = new
                {
                    id = "sender-dm-reject-001",
                    displayName = "Alice",
                },
            }),
        };
        webhookRequest.Headers.Authorization = new AuthenticationHeaderValue("Bearer", GatewayToken);
        webhookRequest.Headers.Add("X-KodaClaw-Webhook-Secret", "hook-secret");

        var webhookResponse = await hosted.Client.SendAsync(webhookRequest);
        webhookResponse.StatusCode.Should().Be(HttpStatusCode.OK);
        var detail = await webhookResponse.Content.ReadFromJsonAsync<ChannelThreadDetail>();
        detail.Should().NotBeNull();
        detail!.PendingApprovalId.Should().NotBeNullOrWhiteSpace();

        var rejectResponse = await hosted.Client.PostAsJsonAsync(
            $"/api/approvals/{detail.PendingApprovalId}/reject",
            new ApprovalDecisionRequest("reject test delivery"));
        rejectResponse.StatusCode.Should().Be(HttpStatusCode.OK);

        var detailAfterReject = await hosted.Client.GetFromJsonAsync<ChannelThreadDetail>(
            $"/api/channels/threads/{detail.Binding.Id}");
        detailAfterReject.Should().NotBeNull();
        detailAfterReject!.PendingApprovalId.Should().BeNull();
        detailAfterReject.HasPendingDraft.Should().BeFalse();
        detailAfterReject.LastTurnOutcome.Should().NotBeNull();
        detailAfterReject.LastTurnOutcome!.Kind.Should().Be(ChannelTurnOutcomeKind.NoAction);
        detailAfterReject.LastTurnOutcome.ReasonCode.Should().Be("approval_rejected");
        detailAfterReject.PolicyEvidence.Should().Contain(item => item.StartsWith("approval_rejected|"));
    }

    private static Task<HostedGateway> StartGatewayAsync(
        string workspaceRoot,
        IModelProvider? modelProvider = null,
        bool enableRuntime = false,
        Action<IServiceCollection>? configureServices = null)
    {
        return HostedGateway.StartAsync(
            gatewayToken: GatewayToken,
            workspaceSnapshot: GatewayAuthIntegrationTests.CreateSnapshot(
                requiresBootstrap: false,
                rootPath: workspaceRoot),
            configureServices: services =>
            {
                if (modelProvider is not null)
                {
                    services.AddSingleton(modelProvider);
                    services.AddSingleton<IModelProvider>(modelProvider);
                }

                configureServices?.Invoke(services);
            },
            configureConfiguration: configuration =>
            {
                var values = new Dictionary<string, string?>
                {
                    ["KODACLAW_WORKSPACE_ROOT"] = workspaceRoot,
                };

                if (enableRuntime)
                {
                    values["KODACLAW_DEFAULT_MODEL"] = "gpt-4o-mini";
                    values["OPENAI_API_KEY"] = "stub-key";
                }

                configuration.AddInMemoryCollection(values);
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

    private sealed class StubChannelTurnModelProvider : IModelProvider
    {
        private readonly string _response;

        public StubChannelTurnModelProvider(string response)
        {
            _response = response;
        }

        public string ProviderName => "stub-channel-turn";

        public async IAsyncEnumerable<StreamChunk> StreamAsync(
            ModelRequest request,
            [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            await Task.Yield();

            yield return new StreamChunk
            {
                Type = StreamChunkType.TextDelta,
                TextDelta = _response,
            };
            yield return new StreamChunk
            {
                Type = StreamChunkType.MessageStop,
                StopReason = ModelStopReason.EndTurn,
                Usage = new TokenUsage
                {
                    InputTokens = 0,
                    OutputTokens = 0,
                },
            };
        }

        public Task<ModelResponse> CompleteAsync(ModelRequest request, CancellationToken cancellationToken = default)
        {
            return Task.FromResult(new ModelResponse
            {
                Content =
                [
                    new TextContent
                    {
                        Text = _response,
                    },
                ],
                StopReason = ModelStopReason.EndTurn,
                Usage = new TokenUsage
                {
                    InputTokens = 0,
                    OutputTokens = 0,
                },
                Model = request.Model,
            });
        }

        public Task<bool> ValidateAsync(CancellationToken cancellationToken = default)
        {
            return Task.FromResult(true);
        }
    }

    private sealed class FakeTelegramApiClient : ITelegramApiClient
    {
        private readonly Queue<IReadOnlyList<TelegramUpdate>> _batches = new();

        public FakeTelegramApiClient(IEnumerable<IReadOnlyList<TelegramUpdate>> batches)
        {
            foreach (var batch in batches)
            {
                _batches.Enqueue(batch);
            }
        }

        public Task<TelegramUser> GetMeAsync(string botToken, CancellationToken cancellationToken = default)
        {
            return Task.FromResult(new TelegramUser
            {
                Id = 90001,
                IsBot = true,
                FirstName = "Koda",
                Username = "koda_bot",
            });
        }

        public async Task<IReadOnlyList<TelegramUpdate>> GetUpdatesAsync(
            string botToken,
            long? offset,
            int timeoutSeconds,
            CancellationToken cancellationToken = default)
        {
            if (_batches.Count > 0)
            {
                return _batches.Dequeue();
            }

            await Task.Delay(10, cancellationToken);
            return [];
        }

        public Task<TelegramSendMessageResult> SendMessageAsync(
            string botToken,
            long chatId,
            string text,
            CancellationToken cancellationToken = default)
        {
            return Task.FromResult(new TelegramSendMessageResult
            {
                MessageId = 1,
            });
        }
    }

    private static TelegramUpdate CreateDirectMessageUpdate(int updateId, int messageId, string text)
    {
        return new TelegramUpdate
        {
            UpdateId = updateId,
            Message = new TelegramMessage
            {
                MessageId = messageId,
                DateUnixSeconds = 1_773_904_800 + updateId,
                Text = text,
                Chat = new TelegramChat
                {
                    Id = 10001,
                    Type = "private",
                    Username = "alice",
                },
                From = new TelegramUser
                {
                    Id = 20001,
                    FirstName = "Alice",
                    Username = "alice",
                },
            },
        };
    }

    private static async Task EventuallyAsync(Func<Task<bool>> predicate, string failureMessage, int attempts = 120, int delayMs = 50)
    {
        for (var attempt = 0; attempt < attempts; attempt++)
        {
            if (await predicate())
            {
                return;
            }

            await Task.Delay(delayMs);
        }

        throw new Xunit.Sdk.XunitException(failureMessage);
    }
}
