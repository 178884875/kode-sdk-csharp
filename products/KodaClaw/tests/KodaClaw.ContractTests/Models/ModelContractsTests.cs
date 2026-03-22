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
