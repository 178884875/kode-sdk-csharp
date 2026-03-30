using System.Text.Json;
using FluentAssertions;
using KodaClaw.Contracts;
using Xunit;

namespace KodaClaw.ContractTests.Models;

public sealed class ModelContractsTests
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    [Fact]
    public void Create_model_endpoint_request_should_json_round_trip()
    {
        var payload = new CreateModelEndpointRequest(
            DisplayName: "Base playground",
            Provider: ModelProviderKind.OpenAI,
            ModelId: "gpt-4o-mini",
            BaseUrl: "https://api.openai.com/v1",
            ApiKeyEnvironmentVariable: "OPENAI_API_KEY",
            ApiKeySecretRef: "env:models:OPENAI_API_KEY",
            Enabled: true,
            Capabilities: ModelCapabilitySet.TextChat | ModelCapabilitySet.ToolCalling);

        var json = JsonSerializer.Serialize(payload, JsonOptions);
        var roundTrip = JsonSerializer.Deserialize<CreateModelEndpointRequest>(json, JsonOptions);

        json.Should().Contain("\"displayName\":\"Base playground\"");
        json.Should().Contain("\"provider\":\"OpenAI\"");
        json.Should().Contain("\"modelId\":\"gpt-4o-mini\"");
        json.Should().Contain("\"baseUrl\":\"https://api.openai.com/v1\"");
        json.Should().Contain("\"apiKeyEnvironmentVariable\":\"OPENAI_API_KEY\"");
        json.Should().Contain("\"apiKeySecretRef\":\"env:models:OPENAI_API_KEY\"");
        roundTrip.Should().Be(payload);
    }

    [Fact]
    public void Update_model_endpoint_request_should_json_round_trip()
    {
        var payload = new UpdateModelEndpointRequest(
            DisplayName: "Claude 3 Opus",
            Provider: ModelProviderKind.Anthropic,
            ModelId: "claude-3-opus",
            BaseUrl: "https://api.anthropic.com/v1",
            ApiKeySecretRef: "keychain:models:claude-3-opus",
            Capabilities: ModelCapabilitySet.TextChat);

        var json = JsonSerializer.Serialize(payload, JsonOptions);
        var roundTrip = JsonSerializer.Deserialize<UpdateModelEndpointRequest>(json, JsonOptions);

        json.Should().Contain("\"displayName\":\"Claude 3 Opus\"");
        json.Should().Contain("\"provider\":\"Anthropic\"");
        json.Should().Contain("\"apiKeySecretRef\":\"keychain:models:claude-3-opus\"");
        json.Should().Contain("\"capabilities\":");
        roundTrip.Should().Be(payload);
    }

    // ── CustomHeaders ─────────────────────────────────────────────────────────

    [Fact]
    public void ModelEndpoint_without_custom_headers_defaults_to_null_for_backward_compat()
    {
        var json = """
            {
              "id": "ep-001", "displayName": "Old Endpoint",
              "provider": "Anthropic", "modelId": "claude-3-5-haiku-20241022",
              "enabled": true, "capabilities": 3,
              "isDefault": false,
              "createdAt": "2025-01-01T00:00:00Z",
              "updatedAt": "2025-01-01T00:00:00Z"
            }
            """;

        var endpoint = JsonSerializer.Deserialize<ModelEndpoint>(json, JsonOptions);

        endpoint.Should().NotBeNull();
        endpoint!.CustomHeaders.Should().BeNull();
    }

    [Fact]
    public void CreateModelEndpointRequest_with_custom_headers_should_round_trip()
    {
        var headers = new Dictionary<string, string> { ["User-Agent"] = "claude-code/0.1.0" };
        var request = new CreateModelEndpointRequest(
            DisplayName: "Spoofed Agent",
            Provider: ModelProviderKind.AnthropicCompatible,
            ModelId: "claude-sonnet-4-6",
            BaseUrl: "https://proxy.example.com",
            CustomHeaders: headers);

        var json = JsonSerializer.Serialize(request, JsonOptions);
        var roundTrip = JsonSerializer.Deserialize<CreateModelEndpointRequest>(json, JsonOptions);

        json.Should().Contain("customHeaders");
        json.Should().Contain("User-Agent");
        json.Should().Contain("claude-code/0.1.0");
        roundTrip.Should().NotBeNull();
        roundTrip!.CustomHeaders.Should().ContainKey("User-Agent")
            .WhoseValue.Should().Be("claude-code/0.1.0");
    }

    [Fact]
    public void UpdateModelEndpointRequest_without_custom_headers_should_round_trip_as_null()
    {
        var request = new UpdateModelEndpointRequest(
            DisplayName: "Plain endpoint",
            Provider: ModelProviderKind.OpenAI,
            ModelId: "gpt-4o");

        var json = JsonSerializer.Serialize(request, JsonOptions);
        var roundTrip = JsonSerializer.Deserialize<UpdateModelEndpointRequest>(json, JsonOptions);

        roundTrip.Should().NotBeNull();
        roundTrip!.CustomHeaders.Should().BeNull();
    }

    [Fact]
    public void Models_query_response_should_contain_items()
    {
        var endpoints = new[]
        {
            new ModelEndpoint(
                Id: "endpoint-001",
                DisplayName: "Multi-model",
                Provider: ModelProviderKind.OpenAICompatible,
                ModelId: "o3.1",
                BaseUrl: "https://proxy.example.com",
                ApiKeyEnvironmentVariable: "PROXY_KEY",
                ApiKeySecretRef: "env:models:PROXY_KEY",
                Enabled: true,
                Capabilities: ModelCapabilitySet.TextChat | ModelCapabilitySet.ToolCalling,
                IsDefault: true,
                CreatedAt: DateTimeOffset.UtcNow,
                UpdatedAt: DateTimeOffset.UtcNow),
        };

        var payload = new ModelsQueryResponse(endpoints);
        var json = JsonSerializer.Serialize(payload, JsonOptions);

        json.Should().Contain("\"items\"");
        json.Should().Contain("\"id\":\"endpoint-001\"");
        json.Should().Contain("\"provider\":\"OpenAICompatible\"");
        json.Should().Contain("\"isDefault\":true");
    }
}
