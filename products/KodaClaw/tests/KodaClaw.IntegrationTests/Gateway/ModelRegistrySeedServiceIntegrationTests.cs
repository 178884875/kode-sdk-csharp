using System.Net.Http.Headers;
using System.Net.Http.Json;
using FluentAssertions;
using KodaClaw.Contracts;
using KodaClaw.ModelHub;
using KodaClaw.Workspace;
using Microsoft.Extensions.Configuration;
using Xunit;

namespace KodaClaw.IntegrationTests.Gateway;

/// <summary>
/// KC-3503: ModelRegistrySeedService integration tests.
/// Verifies that the seed service populates the registry from env-var config
/// when the registry is empty at startup, and that it skips seeding when the
/// registry is already populated.
/// </summary>
public sealed class ModelRegistrySeedServiceIntegrationTests
{
    private const string GatewayToken = "test-token";

    // ── Seeds from env vars when registry is empty ────────────────────────────

    [Fact]
    public async Task Seed_creates_anthropic_endpoint_when_registry_is_empty_and_anthropic_env_var_set()
    {
        using var workspace = new TempWorkspaceRoot("seed-anthropic");

        await using var hosted = await HostedGateway.StartAsync(
            gatewayToken: GatewayToken,
            workspaceSnapshot: GatewayAuthIntegrationTests.CreateSnapshot(
                requiresBootstrap: false, rootPath: workspace.Path),
            configureConfiguration: config =>
            {
                config.AddInMemoryCollection(new Dictionary<string, string?>
                {
                    ["KODACLAW_WORKSPACE_ROOT"] = workspace.Path,
                    ["KODACLAW_DEFAULT_MODEL"] = "claude-sonnet-4-6",
                    // Inject ANTHROPIC_API_KEY directly into in-memory config so
                    // RuntimeConfigurationBootstrap sees it as a configured key.
                    ["ANTHROPIC_API_KEY"] = "sk-ant-test",
                });
            },
            useTestWorkspaceService: false);
        hosted.Client.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", GatewayToken);

        var response = await hosted.Client.GetAsync("/api/models");
        response.EnsureSuccessStatusCode();

        var body = await response.Content.ReadFromJsonAsync<ModelsQueryResponse>();
        body.Should().NotBeNull();
        body!.Items.Should().HaveCount(1,
            because: "seed service should have created one endpoint from env-var config");

        var seeded = body.Items[0];
        seeded.Provider.Should().Be(ModelProviderKind.Anthropic);
        seeded.ModelId.Should().Be("claude-sonnet-4-6");
        seeded.IsDefault.Should().BeTrue(because: "seed service calls SetDefaultAsync");
    }

    [Fact]
    public async Task Seed_creates_openai_endpoint_when_registry_is_empty_and_openai_env_var_set()
    {
        using var workspace = new TempWorkspaceRoot("seed-openai");

        await using var hosted = await HostedGateway.StartAsync(
            gatewayToken: GatewayToken,
            workspaceSnapshot: GatewayAuthIntegrationTests.CreateSnapshot(
                requiresBootstrap: false, rootPath: workspace.Path),
            configureConfiguration: config =>
            {
                config.AddInMemoryCollection(new Dictionary<string, string?>
                {
                    ["KODACLAW_WORKSPACE_ROOT"] = workspace.Path,
                    ["KODACLAW_DEFAULT_MODEL"] = "gpt-4o-mini",
                    ["OPENAI_API_KEY"] = "sk-openai-test",
                });
            },
            useTestWorkspaceService: false);
        hosted.Client.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", GatewayToken);

        var response = await hosted.Client.GetAsync("/api/models");
        response.EnsureSuccessStatusCode();

        var body = await response.Content.ReadFromJsonAsync<ModelsQueryResponse>();
        body.Should().NotBeNull();
        body!.Items.Should().HaveCount(1,
            because: "seed service should have created one endpoint from env-var config");

        var seeded = body.Items[0];
        seeded.Provider.Should().Be(ModelProviderKind.OpenAI);
        seeded.ModelId.Should().Be("gpt-4o-mini");
        seeded.IsDefault.Should().BeTrue();
    }

    // ── Skips seeding when registry already has endpoints ─────────────────────

    [Fact]
    public async Task Seed_skips_when_registry_already_has_endpoints()
    {
        using var workspace = new TempWorkspaceRoot("seed-skip");

        // Pre-populate the registry BEFORE starting the gateway
        var workspaceService = new WorkspaceService(new KodaClawWorkspaceOptions
        {
            RootPath = workspace.Path,
        });
        var repo = new SqliteModelRegistryRepository(workspaceService);
        var now = DateTimeOffset.UtcNow;
        await repo.AddAsync(new ModelEndpoint(
            Id: "model-existing",
            DisplayName: "Pre-existing",
            Provider: ModelProviderKind.OpenAI,
            ModelId: "gpt-4o",
            BaseUrl: null,
            ApiKeyEnvironmentVariable: "OPENAI_API_KEY",
            ApiKeySecretRef: null,
            Enabled: true,
            Capabilities: ModelCapabilitySet.TextChat | ModelCapabilitySet.ToolCalling,
            IsDefault: true,
            CreatedAt: now,
            UpdatedAt: now,
            ContextWindowSize: 128_000));

        await using var hosted = await HostedGateway.StartAsync(
            gatewayToken: GatewayToken,
            workspaceSnapshot: GatewayAuthIntegrationTests.CreateSnapshot(
                requiresBootstrap: false, rootPath: workspace.Path),
            configureConfiguration: config =>
            {
                config.AddInMemoryCollection(new Dictionary<string, string?>
                {
                    ["KODACLAW_WORKSPACE_ROOT"] = workspace.Path,
                    ["KODACLAW_DEFAULT_MODEL"] = "claude-sonnet-4-6",
                    ["ANTHROPIC_API_KEY"] = "sk-ant-skip",
                });
            },
            useTestWorkspaceService: false);
        hosted.Client.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", GatewayToken);

        var response = await hosted.Client.GetAsync("/api/models");
        response.EnsureSuccessStatusCode();

        var body = await response.Content.ReadFromJsonAsync<ModelsQueryResponse>();
        body.Should().NotBeNull();
        body!.Items.Should().HaveCount(1,
            because: "seed service should not add entries when registry already has endpoints");
        body.Items[0].Id.Should().Be("model-existing");
    }

    // ── No env vars = no seed ─────────────────────────────────────────────────

    [Fact]
    public async Task Seed_does_nothing_when_no_env_vars_configured()
    {
        // Ensure relevant env vars are NOT set
        Environment.SetEnvironmentVariable("ANTHROPIC_API_KEY", null);
        Environment.SetEnvironmentVariable("OPENAI_API_KEY", null);

        using var workspace = new TempWorkspaceRoot("seed-empty");

        await using var hosted = await HostedGateway.StartAsync(
            gatewayToken: GatewayToken,
            workspaceSnapshot: GatewayAuthIntegrationTests.CreateSnapshot(
                requiresBootstrap: false, rootPath: workspace.Path),
            configureConfiguration: config =>
            {
                config.AddInMemoryCollection(new Dictionary<string, string?>
                {
                    ["KODACLAW_WORKSPACE_ROOT"] = workspace.Path,
                    // No KODACLAW_DEFAULT_MODEL, no API keys
                });
            },
            useTestWorkspaceService: false);
        hosted.Client.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", GatewayToken);

        var response = await hosted.Client.GetAsync("/api/models");
        response.EnsureSuccessStatusCode();

        var body = await response.Content.ReadFromJsonAsync<ModelsQueryResponse>();
        body.Should().NotBeNull();
        body!.Items.Should().BeEmpty(because: "no env-var config means no seed");
    }

    // ── helpers ───────────────────────────────────────────────────────────────

    private sealed class TempWorkspaceRoot : IDisposable
    {
        public TempWorkspaceRoot(string tag)
        {
            Path = System.IO.Path.Combine(
                System.IO.Path.GetTempPath(),
                $"kodaclaw-seed-{tag}",
                Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(Path);
        }

        public string Path { get; }

        public void Dispose()
        {
            if (Directory.Exists(Path))
                Directory.Delete(Path, recursive: true);
        }
    }
}
