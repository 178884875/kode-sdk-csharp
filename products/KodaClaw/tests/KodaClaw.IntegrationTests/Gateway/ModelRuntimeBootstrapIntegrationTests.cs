using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Runtime.CompilerServices;
using System.Text.Json;
using FluentAssertions;
using KodaClaw.Contracts;
using KodaClaw.ModelHub;
using KodaClaw.Runtime;
using KodaClaw.Workspace;
using Kode.Agent.Sdk.Core.Abstractions;
using Kode.Agent.Sdk.Core.Types;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace KodaClaw.IntegrationTests.Gateway;

public sealed class ModelRuntimeBootstrapIntegrationTests
{
    private const string GatewayToken = "test-token";
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    [Fact]
    public async Task Gateway_should_bootstrap_runtime_from_openai_secret_ref_configuration()
    {
        var environmentKey = $"KODACLAW_RUNTIME_OPENAI_{Guid.NewGuid():N}";
        Environment.SetEnvironmentVariable(environmentKey, "secret-ref-openai-key");

        try
        {
            await using var hosted = await HostedGateway.StartAsync(
                gatewayToken: GatewayToken,
                workspaceSnapshot: GatewayAuthIntegrationTests.CreateSnapshot(requiresBootstrap: false),
                configureConfiguration: configuration =>
                {
                    configuration.AddInMemoryCollection(new Dictionary<string, string?>
                    {
                        ["KODACLAW_DEFAULT_MODEL"] = "gpt-4o-mini",
                        ["OPENAI_API_KEY_SECRET_REF"] = new SecretRef("env", "runtime", environmentKey).ToReferenceString(),
                    });
                });
            hosted.Client.DefaultRequestHeaders.Authorization =
                new AuthenticationHeaderValue("Bearer", GatewayToken);

            hosted.Services.GetService<IMainSessionService>().Should().NotBeNull();
            hosted.Services.GetRequiredService<MainSessionOptions>().Model.Should().Be("gpt-4o-mini");
            // RegistryAwareModelProvider is the singleton; ProviderName reflects the registry-first routing layer.
            hosted.Services.GetRequiredService<IModelProvider>().ProviderName.Should().Be("registry");
        }
        finally
        {
            Environment.SetEnvironmentVariable(environmentKey, null);
        }
    }

    [Fact]
    public async Task Gateway_should_bootstrap_runtime_from_default_model_endpoint_secret_ref()
    {
        using var workspace = new TempWorkspaceRoot();
        var environmentKey = $"KODACLAW_RUNTIME_MODEL_{Guid.NewGuid():N}";
        Environment.SetEnvironmentVariable(environmentKey, "model-endpoint-openai-key");

        try
        {
            var workspaceService = new WorkspaceService(new KodaClawWorkspaceOptions
            {
                RootPath = workspace.Path,
            });
            var repository = new SqliteModelRegistryRepository(workspaceService);
            var now = DateTimeOffset.UtcNow;
            await repository.AddAsync(new ModelEndpoint(
                Id: "model-default",
                DisplayName: "Default endpoint",
                Provider: ModelProviderKind.OpenAICompatible,
                ModelId: "o3",
                BaseUrl: "https://proxy.runtime.test",
                ApiKeyEnvironmentVariable: null,
                ApiKeySecretRef: new SecretRef("env", "models", environmentKey).ToReferenceString(),
                Enabled: true,
                Capabilities: ModelCapabilitySet.TextChat | ModelCapabilitySet.ToolCalling,
                IsDefault: true,
                CreatedAt: now,
                UpdatedAt: now));

            await using var hosted = await HostedGateway.StartAsync(
                gatewayToken: GatewayToken,
                workspaceSnapshot: GatewayAuthIntegrationTests.CreateSnapshot(
                    requiresBootstrap: false,
                    rootPath: workspace.Path),
                configureConfiguration: configuration =>
                {
                    configuration.AddInMemoryCollection(new Dictionary<string, string?>
                    {
                        ["KODACLAW_WORKSPACE_ROOT"] = workspace.Path,
                    });
                },
                useTestWorkspaceService: false);
            hosted.Client.DefaultRequestHeaders.Authorization =
                new AuthenticationHeaderValue("Bearer", GatewayToken);

            hosted.Services.GetService<IMainSessionService>().Should().NotBeNull();
            hosted.Services.GetRequiredService<MainSessionOptions>().Model.Should().Be("o3");
            hosted.Services.GetRequiredService<IModelProvider>().ProviderName.Should().Be("registry");
        }
        finally
        {
            Environment.SetEnvironmentVariable(environmentKey, null);
        }
    }

    [Fact]
    public async Task Gateway_should_activate_chat_after_default_model_is_configured_without_restart()
    {
        using var workspace = new TempWorkspaceRoot();
        var environmentKey = $"KODACLAW_RUNTIME_DYNAMIC_{Guid.NewGuid():N}";
        Environment.SetEnvironmentVariable(environmentKey, "dynamic-openai-key");

        try
        {
            await using var hosted = await HostedGateway.StartAsync(
                gatewayToken: GatewayToken,
                workspaceSnapshot: GatewayAuthIntegrationTests.CreateSnapshot(
                    requiresBootstrap: false,
                    rootPath: workspace.Path),
                configureServices: services =>
                {
                    services.AddSingleton<IRuntimeModelProviderFactory>(new StubRuntimeModelProviderFactory());
                },
                configureConfiguration: configuration =>
                {
                    configuration.AddInMemoryCollection(new Dictionary<string, string?>
                    {
                        ["KODACLAW_WORKSPACE_ROOT"] = workspace.Path,
                    });
                },
                useTestWorkspaceService: false);
            hosted.Client.DefaultRequestHeaders.Authorization =
                new AuthenticationHeaderValue("Bearer", GatewayToken);

            var initialEvents = await ReadChatEventsAsync(hosted.Client, "before config");
            initialEvents.Should().ContainSingle();
            initialEvents[0].Type.Should().Be("error");
            initialEvents[0].Error.Should().NotBeNull();
            initialEvents[0].Error!.Code.Should().Be("runtime.error");
            initialEvents[0].Error!.Message.Should().Contain("not configured");

            var createResponse = await hosted.Client.PostAsJsonAsync(
                "/api/models",
                new CreateModelEndpointRequest(
                    DisplayName: "Dynamic OpenAI",
                    Provider: ModelProviderKind.OpenAI,
                    ModelId: "gpt-4o-mini",
                    ApiKeyEnvironmentVariable: environmentKey,
                    Enabled: true,
                    Capabilities: ModelCapabilitySet.TextChat | ModelCapabilitySet.ToolCalling));
            createResponse.EnsureSuccessStatusCode();

            var created = await createResponse.Content.ReadFromJsonAsync<ModelEndpoint>();
            created.Should().NotBeNull();

            var defaultResponse = await hosted.Client.PostAsync($"/api/models/{created!.Id}/default", content: null);
            defaultResponse.EnsureSuccessStatusCode();

            var activatedEvents = await ReadChatEventsAsync(hosted.Client, "after config");
            activatedEvents.Select(item => item.Type).Should().ContainInOrder("text_chunk", "done");
            activatedEvents[0].Delta.Should().Be("stub");
        }
        finally
        {
            Environment.SetEnvironmentVariable(environmentKey, null);
        }
    }

    private static async Task<IReadOnlyList<ChatStreamEvent>> ReadChatEventsAsync(HttpClient client, string message)
    {
        using var response = await client.PostAsJsonAsync("/api/chat/stream", new { message });
        response.EnsureSuccessStatusCode();

        var body = await response.Content.ReadAsStringAsync();
        return body
            .Split("\n\n", StringSplitOptions.RemoveEmptyEntries)
            .Select(chunk =>
            {
                var lines = chunk
                    .Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
                var data = lines.Single(line => line.StartsWith("data: ", StringComparison.Ordinal))["data: ".Length..];
                return JsonSerializer.Deserialize<ChatStreamEvent>(data, JsonOptions);
            })
            .Where(static item => item is not null)
            .Cast<ChatStreamEvent>()
            .ToArray();
    }

    private sealed class TempWorkspaceRoot : IDisposable
    {
        public TempWorkspaceRoot()
        {
            Path = System.IO.Path.Combine(
                System.IO.Path.GetTempPath(),
                "kodaclaw-runtime-bootstrap",
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

    private sealed class StubRuntimeModelProviderFactory : IRuntimeModelProviderFactory
    {
        public IModelProvider Create(RuntimeProviderKind kind, RuntimeConfigurationSnapshot snapshot)
        {
            return new StubModelProvider();
        }
    }

    private sealed class StubModelProvider : IModelProvider
    {
        public string ProviderName => "stub";

        public async IAsyncEnumerable<StreamChunk> StreamAsync(
            ModelRequest request,
            [EnumeratorCancellation] CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            await Task.Yield();

            yield return new StreamChunk
            {
                Type = StreamChunkType.TextDelta,
                TextDelta = "stub",
            };

            yield return new StreamChunk
            {
                Type = StreamChunkType.MessageStop,
                StopReason = ModelStopReason.EndTurn,
                Usage = new TokenUsage
                {
                    InputTokens = 1,
                    OutputTokens = 1,
                },
            };
        }

        public Task<ModelResponse> CompleteAsync(ModelRequest request, CancellationToken cancellationToken = default)
        {
            return Task.FromResult(new ModelResponse
            {
                Model = request.Model,
                StopReason = ModelStopReason.EndTurn,
                Usage = new TokenUsage
                {
                    InputTokens = 1,
                    OutputTokens = 1,
                },
                Content =
                [
                    new TextContent
                    {
                        Text = "stub",
                    },
                ],
            });
        }

        public Task<bool> ValidateAsync(CancellationToken cancellationToken = default)
        {
            return Task.FromResult(true);
        }
    }
}
