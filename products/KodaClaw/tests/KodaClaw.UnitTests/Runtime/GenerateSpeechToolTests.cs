using System.Text.Json;
using FluentAssertions;
using KodaClaw.ModelHub;
using KodaClaw.Runtime;
using Kode.Agent.Sdk.Core.Abstractions;
using Moq;
using Xunit;

namespace KodaClaw.UnitTests.Runtime;

public sealed class GenerateSpeechToolTests
{
    private readonly Mock<ISpeechService> _speechServiceMock = new();
    private readonly GenerateSpeechTool _tool;

    public GenerateSpeechToolTests()
    {
        _tool = new GenerateSpeechTool(_speechServiceMock.Object);
    }

    // ── Metadata ──────────────────────────────────────────────────────────────

    [Fact]
    public void Name_ReturnsGenerateSpeech()
        => _tool.Name.Should().Be("generate_speech");

    [Fact]
    public void Attributes_ReadOnlyFalse_RequiresApprovalFalse()
    {
        _tool.Attributes.ReadOnly.Should().BeFalse();
        _tool.Attributes.RequiresApproval.Should().BeFalse();
    }

    // ── Successful generation ─────────────────────────────────────────────────

    [Fact]
    public async Task RunAsync_ValidText_CallsServiceAndReturnsMediaId()
    {
        _speechServiceMock
            .Setup(s => s.GenerateSpeechAsync("今日天气晴好", null, null, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new GenerateSpeechResult("media-abc123", "audio/mpeg"));

        var result = await ExecuteAsync(new GenerateSpeechArgs { Text = "今日天气晴好" });

        result.Success.Should().BeTrue();
        var json = JsonSerializer.Deserialize<JsonElement>(
            JsonSerializer.Serialize(result.Value, new JsonSerializerOptions(JsonSerializerDefaults.Web)));
        json.GetProperty("ok").GetBoolean().Should().BeTrue();
        json.GetProperty("mediaId").GetString().Should().Be("media-abc123");
        json.GetProperty("mediaUrl").GetString().Should().Be("/api/media/media-abc123");
        json.GetProperty("contentType").GetString().Should().Be("audio/mpeg");
    }

    [Fact]
    public async Task RunAsync_WithVoice_PassesVoiceToService()
    {
        _speechServiceMock
            .Setup(s => s.GenerateSpeechAsync("hello", "female", null, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new GenerateSpeechResult("media-voice-001", "audio/mpeg"));

        var result = await ExecuteAsync(new GenerateSpeechArgs { Text = "hello", Voice = "female" });

        result.Success.Should().BeTrue();
        _speechServiceMock.Verify(
            s => s.GenerateSpeechAsync("hello", "female", null, It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Fact]
    public async Task RunAsync_WithEndpointId_PassesEndpointIdToService()
    {
        _speechServiceMock
            .Setup(s => s.GenerateSpeechAsync("test", null, "mimo-tts-default", It.IsAny<CancellationToken>()))
            .ReturnsAsync(new GenerateSpeechResult("media-ep-001", "audio/mpeg"));

        var result = await ExecuteAsync(new GenerateSpeechArgs
        {
            Text = "test",
            EndpointId = "mimo-tts-default",
        });

        result.Success.Should().BeTrue();
        _speechServiceMock.Verify(
            s => s.GenerateSpeechAsync("test", null, "mimo-tts-default", It.IsAny<CancellationToken>()),
            Times.Once);
    }

    // ── Error handling ────────────────────────────────────────────────────────

    [Fact]
    public async Task RunAsync_NoTtsEndpoint_ReturnsFail()
    {
        _speechServiceMock
            .Setup(s => s.GenerateSpeechAsync(It.IsAny<string>(), It.IsAny<string?>(), It.IsAny<string?>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException(
                "No enabled TextToSpeech endpoint found. Please configure a TTS model in ModelsSettingsDesk."));

        var result = await ExecuteAsync(new GenerateSpeechArgs { Text = "hello" });

        result.Success.Should().BeFalse();
        result.Error.Should().Contain("No enabled TextToSpeech endpoint found");
    }

    [Fact]
    public async Task RunAsync_ApiError_ReturnsFail()
    {
        _speechServiceMock
            .Setup(s => s.GenerateSpeechAsync(It.IsAny<string>(), It.IsAny<string?>(), It.IsAny<string?>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("TTS API error 401: Unauthorized"));

        var result = await ExecuteAsync(new GenerateSpeechArgs { Text = "hello" });

        result.Success.Should().BeFalse();
        result.Error.Should().Contain("401");
    }

    // ── Helpers ───────────────────────────────────────────────────────────────

    private Task<ToolResult> ExecuteAsync(GenerateSpeechArgs args)
    {
        var context = new ToolContext
        {
            AgentId = "test-agent",
            CallId = "test-call",
            Sandbox = new Mock<ISandbox>().Object,
        };
        return _tool.ExecuteAsync((object)args, context, CancellationToken.None);
    }
}
