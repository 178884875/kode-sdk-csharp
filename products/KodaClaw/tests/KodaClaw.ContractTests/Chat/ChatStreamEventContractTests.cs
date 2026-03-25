using System.Text.Json;
using FluentAssertions;
using KodaClaw.Contracts;
using Xunit;

namespace KodaClaw.ContractTests.Chat;

public sealed class ChatStreamEventContractTests
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    [Fact]
    public void Session_rotated_event_should_serialize_with_correct_type_and_reason()
    {
        var evt = new ChatStreamEvent(
            Type: "session_rotated",
            SessionId: "main-20260325-abc123",
            Reason: "workspace_updated");

        var json = JsonSerializer.Serialize(evt, JsonOptions);
        var doc = JsonDocument.Parse(json).RootElement;

        doc.GetProperty("type").GetString().Should().Be("session_rotated");
        doc.GetProperty("sessionId").GetString().Should().Be("main-20260325-abc123");
        doc.GetProperty("reason").GetString().Should().Be("workspace_updated");
    }

    [Fact]
    public void Session_rotated_event_should_round_trip_through_json()
    {
        var original = new ChatStreamEvent(
            Type: "session_rotated",
            SessionId: "main-20260325-abc123",
            Reason: "workspace_updated");

        var json = JsonSerializer.Serialize(original, JsonOptions);
        var restored = JsonSerializer.Deserialize<ChatStreamEvent>(json, JsonOptions);

        restored.Should().NotBeNull();
        restored!.Type.Should().Be("session_rotated");
        restored.SessionId.Should().Be("main-20260325-abc123");
        restored.Reason.Should().Be("workspace_updated");
        restored.Delta.Should().BeNull();
        restored.Error.Should().BeNull();
    }

    [Fact]
    public void Text_chunk_event_should_serialize_correctly()
    {
        var evt = new ChatStreamEvent(
            Type: "text_chunk",
            SessionId: "main-1",
            Step: 1,
            Sequence: 42,
            Timestamp: 1711234567890,
            Delta: "hello");

        var json = JsonSerializer.Serialize(evt, JsonOptions);
        var doc = JsonDocument.Parse(json).RootElement;

        doc.GetProperty("type").GetString().Should().Be("text_chunk");
        doc.GetProperty("delta").GetString().Should().Be("hello");
        doc.GetProperty("step").GetInt32().Should().Be(1);
        doc.GetProperty("sequence").GetInt64().Should().Be(42);
    }

    [Fact]
    public void Done_event_should_serialize_correctly()
    {
        var evt = new ChatStreamEvent(
            Type: "done",
            SessionId: "main-1",
            Reason: "end_turn");

        var json = JsonSerializer.Serialize(evt, JsonOptions);
        var doc = JsonDocument.Parse(json).RootElement;

        doc.GetProperty("type").GetString().Should().Be("done");
        doc.GetProperty("reason").GetString().Should().Be("end_turn");
    }
}
