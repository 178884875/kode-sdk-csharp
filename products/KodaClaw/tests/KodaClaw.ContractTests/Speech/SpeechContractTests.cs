using System.Text.Json;
using FluentAssertions;
using KodaClaw.Contracts;
using KodaClaw.ModelHub;
using Xunit;

namespace KodaClaw.ContractTests.Speech;

public sealed class SpeechContractTests
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    // ── GenerateSpeechResult ──────────────────────────────────────────────────

    [Fact]
    public void GenerateSpeechResult_should_json_round_trip()
    {
        var result = new GenerateSpeechResult(
            MediaId: "media-tts-abc123",
            ContentType: "audio/mpeg");

        var json = JsonSerializer.Serialize(result, JsonOptions);
        var roundTrip = JsonSerializer.Deserialize<GenerateSpeechResult>(json, JsonOptions);

        json.Should().Contain("\"mediaId\":\"media-tts-abc123\"");
        json.Should().Contain("\"contentType\":\"audio/mpeg\"");
        roundTrip.Should().Be(result);
    }

    [Fact]
    public void GenerateSpeechResult_ContentType_is_audio_mpeg()
    {
        var result = new GenerateSpeechResult("media-001", "audio/mpeg");
        result.ContentType.Should().Be("audio/mpeg");
    }

    // ── ModelCapabilitySet.TextToSpeech ────────────────────────────────────────

    [Fact]
    public void TextToSpeech_capability_flag_value_is_16()
    {
        ((int)ModelCapabilitySet.TextToSpeech).Should().Be(16);
    }

    [Fact]
    public void TtsModelEndpoint_capabilities_should_include_TextToSpeech()
    {
        var now = DateTimeOffset.UtcNow;
        var endpoint = new ModelEndpoint(
            Id: "mimo-tts-default",
            DisplayName: "MiMo V2 TTS",
            Provider: ModelProviderKind.OpenAICompatible,
            ModelId: "MiMo-V2-TTS",
            BaseUrl: "https://api.xiaomimimo.com/v1",
            ApiKeyEnvironmentVariable: "MIMO_API_KEY",
            ApiKeySecretRef: null,
            Enabled: true,
            Capabilities: ModelCapabilitySet.TextToSpeech,
            IsDefault: false,
            CreatedAt: now,
            UpdatedAt: now);

        endpoint.Capabilities.HasFlag(ModelCapabilitySet.TextToSpeech).Should().BeTrue();
        endpoint.Capabilities.HasFlag(ModelCapabilitySet.TextChat).Should().BeFalse();
    }

    // ── ISpeechService interface contract ─────────────────────────────────────

    [Fact]
    public void ISpeechService_has_GenerateSpeechAsync_method()
    {
        var method = typeof(ISpeechService).GetMethod("GenerateSpeechAsync");
        method.Should().NotBeNull();

        var parameters = method!.GetParameters();
        parameters.Should().HaveCount(4);
        parameters[0].Name.Should().Be("text");
        parameters[1].Name.Should().Be("voice");
        parameters[2].Name.Should().Be("endpointId");
        parameters[3].Name.Should().Be("cancellationToken");
    }
}
