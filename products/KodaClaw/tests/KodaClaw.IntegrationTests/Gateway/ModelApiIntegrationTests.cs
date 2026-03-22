using System.Collections.Generic;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using FluentAssertions;
using KodaClaw.Contracts;
using Microsoft.Extensions.Configuration;
using Xunit;

namespace KodaClaw.IntegrationTests.Gateway;

public sealed class ModelApiIntegrationTests
{
    private const string GatewayToken = "test-token";

    [Fact]
    public async Task Models_list_should_require_token()
    {
        await using var hosted = await HostedGateway.StartAsync(
            gatewayToken: GatewayToken,
            workspaceSnapshot: GatewayAuthIntegrationTests.CreateSnapshot(requiresBootstrap: false));

        var response = await hosted.Client.GetAsync("/api/models");

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task Models_list_should_return_seeded_items()
    {
        using var workspace = new TempWorkspaceRoot();
        await using var hosted = await StartGatewayAsync(workspace.Path);
        hosted.Client.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", GatewayToken);

        var created = await CreateModelEndpointAsync(hosted.Client, "Model 001");

        var response = await hosted.Client.GetAsync("/api/models");
        response.StatusCode.Should().Be(HttpStatusCode.OK);

        var payload = await response.Content.ReadFromJsonAsync<ModelsQueryResponse>();
        payload.Should().NotBeNull();
        payload!.Items.Should().ContainSingle(item => item.Id == created.Id);
    }

    [Fact]
    public async Task Model_detail_should_return_not_found_for_missing()
    {
        using var workspace = new TempWorkspaceRoot();
        await using var hosted = await StartGatewayAsync(workspace.Path);
        hosted.Client.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", GatewayToken);

        var response = await hosted.Client.GetAsync("/api/models/missing");
        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task Create_model_endpoint_should_persist()
    {
        using var workspace = new TempWorkspaceRoot();
        await using var hosted = await StartGatewayAsync(workspace.Path);
        hosted.Client.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", GatewayToken);

        var request = new CreateModelEndpointRequest(
            DisplayName: "New endpoint",
            Provider: ModelProviderKind.OpenAICompatible,
            ModelId: "o3",
            BaseUrl: "https://proxy.test",
            ApiKeyEnvironmentVariable: "PROXY_KEY",
            ApiKeySecretRef: "env:models:PROXY_KEY");

        var response = await hosted.Client.PostAsJsonAsync("/api/models", request);
        response.StatusCode.Should().Be(HttpStatusCode.Created);

        var payload = await response.Content.ReadFromJsonAsync<ModelEndpoint>();
        payload.Should().NotBeNull();
        payload!.DisplayName.Should().Be("New endpoint");
        payload.ApiKeySecretRef.Should().Be("env:models:PROXY_KEY");
    }

    [Fact]
    public async Task Update_model_endpoint_should_change_fields()
    {
        using var workspace = new TempWorkspaceRoot();
        await using var hosted = await StartGatewayAsync(workspace.Path);
        hosted.Client.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", GatewayToken);
        var created = await CreateModelEndpointAsync(hosted.Client, "Before update");

        var request = new UpdateModelEndpointRequest(
            DisplayName: "Updated name",
            Provider: ModelProviderKind.AnthropicCompatible,
            ModelId: "claude-3.1",
            BaseUrl: "https://anthropic.proxy.test",
            ApiKeySecretRef: "keychain:models:claude-3.1",
            Capabilities: ModelCapabilitySet.TextChat);

        var response = await hosted.Client.PutAsJsonAsync($"/api/models/{created.Id}", request);
        response.StatusCode.Should().Be(HttpStatusCode.OK);

        var payload = await response.Content.ReadFromJsonAsync<ModelEndpoint>();
        payload.Should().NotBeNull();
        payload!.DisplayName.Should().Be("Updated name");
        payload.Provider.Should().Be(ModelProviderKind.AnthropicCompatible);
        payload.ModelId.Should().Be("claude-3.1");
        payload.ApiKeySecretRef.Should().Be("keychain:models:claude-3.1");
        payload.SupportsToolCalling.Should().BeFalse();
    }

    [Fact]
    public async Task Delete_model_endpoint_should_remove_resource()
    {
        using var workspace = new TempWorkspaceRoot();
        await using var hosted = await StartGatewayAsync(workspace.Path);
        hosted.Client.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", GatewayToken);

        var created = await CreateModelEndpointAsync(hosted.Client, "Delete candidate");

        var response = await hosted.Client.DeleteAsync($"/api/models/{created.Id}");
        response.StatusCode.Should().Be(HttpStatusCode.NoContent);

        var detail = await hosted.Client.GetAsync($"/api/models/{created.Id}");
        detail.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task Default_model_endpoint_should_mark_endpoint()
    {
        using var workspace = new TempWorkspaceRoot();
        await using var hosted = await StartGatewayAsync(workspace.Path);
        hosted.Client.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", GatewayToken);

        var first = await CreateModelEndpointAsync(hosted.Client, "Default first");
        var second = await CreateModelEndpointAsync(hosted.Client, "Default second");

        var response = await hosted.Client.PostAsync($"/api/models/{second.Id}/default", null);
        response.StatusCode.Should().Be(HttpStatusCode.OK);

        var payload = await response.Content.ReadFromJsonAsync<ModelEndpoint>();
        payload.Should().NotBeNull();
        payload!.IsDefault.Should().BeTrue();
        payload.Id.Should().Be(second.Id);

        var listResponse = await hosted.Client.GetAsync("/api/models");
        listResponse.StatusCode.Should().Be(HttpStatusCode.OK);
        var listPayload = await listResponse.Content.ReadFromJsonAsync<ModelsQueryResponse>();
        listPayload.Should().NotBeNull();
        listPayload!.Items.Single(item => item.Id == second.Id).IsDefault.Should().BeTrue();
        listPayload.Items.Single(item => item.Id == first.Id).IsDefault.Should().BeFalse();
    }

    [Fact]
    public async Task Invalid_create_request_should_return_bad_request()
    {
        using var workspace = new TempWorkspaceRoot();
        await using var hosted = await StartGatewayAsync(workspace.Path);
        hosted.Client.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", GatewayToken);

        var request = new CreateModelEndpointRequest(
            DisplayName: "",
            Provider: ModelProviderKind.OpenAI,
            ModelId: "");

        var response = await hosted.Client.PostAsJsonAsync("/api/models", request);
        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    private static Task<HostedGateway> StartGatewayAsync(
        string workspaceRoot)
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

    private static async Task<ModelEndpoint> CreateModelEndpointAsync(HttpClient client, string displayName)
    {
        var request = new CreateModelEndpointRequest(
            DisplayName: displayName,
            Provider: ModelProviderKind.OpenAICompatible,
            ModelId: "o3",
            BaseUrl: "https://proxy",
            ApiKeyEnvironmentVariable: "MODEL_KEY",
            ApiKeySecretRef: "env:models:MODEL_KEY");

        var response = await client.PostAsJsonAsync("/api/models", request);
        response.StatusCode.Should().Be(HttpStatusCode.Created);

        var payload = await response.Content.ReadFromJsonAsync<ModelEndpoint>();
        payload.Should().NotBeNull();
        payload!.Id.Should().NotBeNullOrWhiteSpace();
        return payload;
    }

    private sealed class TempWorkspaceRoot : IDisposable
    {
        public TempWorkspaceRoot()
        {
            Path = System.IO.Path.Combine(
                System.IO.Path.GetTempPath(),
                "kodaclaw-model-api",
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
