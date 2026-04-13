using System.Net;
using System.Text;
using System.Text.Json;
using FluentAssertions;
using Kode.Agent.Sdk.Core.Abstractions;
using Kode.Agent.Sdk.Core.Types;
using Kode.Agent.Sdk.Infrastructure.Providers;
using Xunit;

namespace Kode.Agent.Tests.Unit;

/// <summary>
/// Tests for <see cref="OpenAIResponsesProvider"/>.
///
/// Strategy:
///  1. Request-format tests — capture the raw HTTP body and assert on the JSON shape.
///  2. Response-parsing tests — return fake JSON/SSE and assert on the parsed <see cref="ModelResponse"/> / <see cref="StreamChunk"/>s.
/// </summary>
public sealed class OpenAIResponsesProviderTests
{
    // =========================================================================
    // Helpers
    // =========================================================================

    private static (OpenAIResponsesProvider Provider, List<string> CapturedBodies)
        CreateProviderWithCapture(string fakeResponseJson)
    {
        var bodies = new List<string>();
        var handler = new CapturingHttpMessageHandler(bodies, fakeResponseJson);
        var provider = new OpenAIResponsesProvider(
            handler,
            new OpenAIResponsesOptions { ApiKey = "test-key" });
        return (provider, bodies);
    }

    private static (OpenAIResponsesProvider Provider, List<string> CapturedBodies)
        CreateProviderWithSseCapture(string fakeSseResponse)
    {
        var bodies = new List<string>();
        var handler = new CapturingHttpMessageHandler(bodies, fakeSseResponse);
        var provider = new OpenAIResponsesProvider(
            handler,
            new OpenAIResponsesOptions { ApiKey = "test-key" });
        return (provider, bodies);
    }

    private static ModelRequest SimpleRequest(string model = "gpt-4o") => new()
    {
        Model = model,
        Messages = [Message.User("Hello")],
        MaxTokens = 50
    };

    private const string FakeTextResponseJson = """
        {
          "id": "resp_test",
          "object": "response",
          "model": "gpt-4o-2024-11-20",
          "status": "completed",
          "output": [
            {
              "type": "message",
              "id": "msg_test",
              "role": "assistant",
              "status": "completed",
              "content": [
                { "type": "output_text", "text": "Hello there!" }
              ]
            }
          ],
          "usage": { "input_tokens": 10, "output_tokens": 5, "total_tokens": 15 }
        }
        """;

    private const string FakeFunctionCallResponseJson = """
        {
          "id": "resp_fc",
          "object": "response",
          "model": "gpt-4o-2024-11-20",
          "status": "completed",
          "output": [
            {
              "type": "function_call",
              "id": "fc_abc",
              "call_id": "call_abc123",
              "name": "get_weather",
              "arguments": "{\"city\":\"London\"}"
            }
          ],
          "usage": { "input_tokens": 20, "output_tokens": 10, "total_tokens": 30 }
        }
        """;

    private const string FakeIncompleteResponseJson = """
        {
          "id": "resp_inc",
          "object": "response",
          "model": "gpt-4o-2024-11-20",
          "status": "incomplete",
          "incomplete_details": { "reason": "max_output_tokens" },
          "output": [
            {
              "type": "message",
              "id": "msg_inc",
              "role": "assistant",
              "status": "incomplete",
              "content": [
                { "type": "output_text", "text": "Truncated..." }
              ]
            }
          ],
          "usage": { "input_tokens": 10, "output_tokens": 100, "total_tokens": 110 }
        }
        """;

    // =========================================================================
    // 1. Request format tests
    // =========================================================================

    [Fact]
    public async Task CompleteAsync_TextMessage_SendsCorrectInputFormat()
    {
        var (provider, bodies) = CreateProviderWithCapture(FakeTextResponseJson);

        await provider.CompleteAsync(SimpleRequest());

        bodies.Should().ContainSingle();
        using var doc = JsonDocument.Parse(bodies[0]);
        var root = doc.RootElement;

        root.GetProperty("model").GetString().Should().Be("gpt-4o");
        root.GetProperty("input").GetArrayLength().Should().Be(1);

        var first = root.GetProperty("input").EnumerateArray().First();
        first.GetProperty("role").GetString().Should().Be("user");
        first.GetProperty("content").GetString().Should().Be("Hello");
    }

    [Fact]
    public async Task CompleteAsync_WithSystemPrompt_UsesInstructionsField()
    {
        var (provider, bodies) = CreateProviderWithCapture(FakeTextResponseJson);

        await provider.CompleteAsync(new ModelRequest
        {
            Model = "gpt-4o",
            SystemPrompt = "You are a helpful assistant.",
            Messages = [Message.User("Hi")],
            MaxTokens = 10
        });

        using var doc = JsonDocument.Parse(bodies[0]);
        doc.RootElement.GetProperty("instructions").GetString()
            .Should().Be("You are a helpful assistant.");
        // System messages must NOT appear in input array
        var inputRoles = doc.RootElement.GetProperty("input").EnumerateArray()
            .Select(m => m.TryGetProperty("role", out var r) ? r.GetString() : null)
            .ToList();
        inputRoles.Should().NotContain("system");
    }

    [Fact]
    public async Task CompleteAsync_MaxTokens_MapsToMaxOutputTokens()
    {
        var (provider, bodies) = CreateProviderWithCapture(FakeTextResponseJson);

        await provider.CompleteAsync(new ModelRequest
        {
            Model = "gpt-4o",
            Messages = [Message.User("Hi")],
            MaxTokens = 512
        });

        using var doc = JsonDocument.Parse(bodies[0]);
        doc.RootElement.GetProperty("max_output_tokens").GetInt32().Should().Be(512);
    }

    [Fact]
    public async Task CompleteAsync_WithTools_SendsCorrectToolFormat()
    {
        var (provider, bodies) = CreateProviderWithCapture(FakeTextResponseJson);

        await provider.CompleteAsync(new ModelRequest
        {
            Model = "gpt-4o",
            Messages = [Message.User("What is the weather?")],
            MaxTokens = 100,
            Tools =
            [
                new ToolSchema
                {
                    Name = "get_weather",
                    Description = "Get current weather",
                    InputSchema = new { type = "object", properties = new { city = new { type = "string" } } }
                }
            ]
        });

        using var doc = JsonDocument.Parse(bodies[0]);
        var tools = doc.RootElement.GetProperty("tools");
        tools.GetArrayLength().Should().Be(1);

        var tool = tools.EnumerateArray().First();
        tool.GetProperty("type").GetString().Should().Be("function");
        tool.GetProperty("name").GetString().Should().Be("get_weather");
        tool.GetProperty("description").GetString().Should().Be("Get current weather");
        tool.TryGetProperty("parameters", out _).Should().BeTrue();
    }

    [Fact]
    public async Task CompleteAsync_AssistantMessageWithToolCall_SendsFunctionCallContent()
    {
        var (provider, bodies) = CreateProviderWithCapture(FakeTextResponseJson);

        await provider.CompleteAsync(new ModelRequest
        {
            Model = "gpt-4o",
            Messages =
            [
                Message.User("Search for cats"),
                Message.Assistant(new ToolUseContent
                {
                    Id = "call_xyz",
                    Name = "web_search",
                    Input = new { query = "cats" }
                }),
                new Message
                {
                    Role = MessageRole.User,
                    Content = [new ToolResultContent { ToolUseId = "call_xyz", Content = "Found 100 results" }]
                }
            ],
            MaxTokens = 50
        });

        using var doc = JsonDocument.Parse(bodies[0]);
        var input = doc.RootElement.GetProperty("input").EnumerateArray().ToList();

        // user → function_call (top-level) → function_call_output (top-level)
        input.Should().HaveCount(3);

        input[0].GetProperty("role").GetString().Should().Be("user");

        // function_call is a top-level item, not wrapped in role:assistant
        input[1].GetProperty("type").GetString().Should().Be("function_call");
        input[1].GetProperty("id").GetString().Should().StartWith("fc_");
        input[1].GetProperty("call_id").GetString().Should().Be("call_xyz");
        input[1].GetProperty("name").GetString().Should().Be("web_search");

        // function_call_output is a top-level item, not wrapped in role:tool
        input[2].GetProperty("type").GetString().Should().Be("function_call_output");
        input[2].GetProperty("call_id").GetString().Should().Be("call_xyz");
        input[2].GetProperty("output").GetString().Should().Contain("Found 100 results");
    }

    [Fact]
    public async Task CompleteAsync_WithImageContent_SendsInputImageParts()
    {
        var (provider, bodies) = CreateProviderWithCapture(FakeTextResponseJson);

        await provider.CompleteAsync(new ModelRequest
        {
            Model = "gpt-4o",
            Messages =
            [
                new Message
                {
                    Role = MessageRole.User,
                    Content =
                    [
                        new TextContent { Text = "Describe this:" },
                        ImageContent.FromUrl("https://example.com/img.png")
                    ]
                }
            ],
            MaxTokens = 50
        });

        using var doc = JsonDocument.Parse(bodies[0]);
        var input = doc.RootElement.GetProperty("input").EnumerateArray().First();
        var parts = input.GetProperty("content").EnumerateArray().ToList();

        parts.Should().HaveCount(2);
        parts[0].GetProperty("type").GetString().Should().Be("input_text");
        parts[1].GetProperty("type").GetString().Should().Be("input_image");
        parts[1].GetProperty("image_url").GetString().Should().Be("https://example.com/img.png");
    }

    [Fact]
    public async Task StreamAsync_StreamFlagSetToTrue()
    {
        var sseBody = BuildSseBody(
            BuildTextDeltaEvent("Hi"),
            BuildDoneEvent("completed"));
        var (provider, bodies) = CreateProviderWithSseCapture(sseBody);

        await foreach (var _ in provider.StreamAsync(SimpleRequest())) { }

        using var doc = JsonDocument.Parse(bodies[0]);
        doc.RootElement.GetProperty("stream").GetBoolean().Should().BeTrue();
    }

    [Fact]
    public async Task CompleteAsync_StreamFlagNotPresent()
    {
        var (provider, bodies) = CreateProviderWithCapture(FakeTextResponseJson);

        await provider.CompleteAsync(SimpleRequest());

        using var doc = JsonDocument.Parse(bodies[0]);
        doc.RootElement.TryGetProperty("stream", out _).Should().BeFalse();
    }

    // =========================================================================
    // 2. Response parsing tests (CompleteAsync)
    // =========================================================================

    [Fact]
    public async Task CompleteAsync_TextResponse_ParsesCorrectly()
    {
        var (provider, _) = CreateProviderWithCapture(FakeTextResponseJson);

        var response = await provider.CompleteAsync(SimpleRequest());

        response.Model.Should().Be("gpt-4o-2024-11-20");
        response.StopReason.Should().Be(ModelStopReason.EndTurn);
        response.Usage.InputTokens.Should().Be(10);
        response.Usage.OutputTokens.Should().Be(5);
        response.Content.Should().ContainSingle().Which.Should().BeOfType<TextContent>()
            .Which.Text.Should().Be("Hello there!");
    }

    [Fact]
    public async Task CompleteAsync_FunctionCallResponse_ParsesCorrectly()
    {
        var (provider, _) = CreateProviderWithCapture(FakeFunctionCallResponseJson);

        var response = await provider.CompleteAsync(SimpleRequest());

        response.StopReason.Should().Be(ModelStopReason.ToolUse);
        response.Content.Should().ContainSingle().Which.Should().BeOfType<ToolUseContent>()
            .Which.Should().BeEquivalentTo(new { Id = "call_abc123", Name = "get_weather" });
    }

    [Fact]
    public async Task CompleteAsync_IncompleteStatus_ReturnsMaxTokensReason()
    {
        var (provider, _) = CreateProviderWithCapture(FakeIncompleteResponseJson);

        var response = await provider.CompleteAsync(SimpleRequest());

        response.StopReason.Should().Be(ModelStopReason.MaxTokens);
    }

    // =========================================================================
    // 3. Streaming tests
    // =========================================================================

    [Fact]
    public async Task StreamAsync_TextDeltas_YieldsTextChunks()
    {
        var sseBody = BuildSseBody(
            BuildTextDeltaEvent("Hello"),
            BuildTextDeltaEvent(" world"),
            BuildDoneEvent("completed", inputTokens: 5, outputTokens: 3));
        var (provider, _) = CreateProviderWithSseCapture(sseBody);

        var chunks = new List<StreamChunk>();
        await foreach (var chunk in provider.StreamAsync(SimpleRequest()))
            chunks.Add(chunk);

        var textDeltas = chunks.Where(c => c.Type == StreamChunkType.TextDelta).ToList();
        textDeltas.Should().HaveCount(2);
        textDeltas[0].TextDelta.Should().Be("Hello");
        textDeltas[1].TextDelta.Should().Be(" world");
    }

    [Fact]
    public async Task StreamAsync_FunctionCall_YieldsToolUseChunks()
    {
        var sseBody = BuildSseBody(
            BuildFunctionCallAddedEvent("fc_1", "call_1", "search"),
            BuildFunctionCallDeltaEvent("fc_1", "call_1", "{\"q\":"),
            BuildFunctionCallDeltaEvent("fc_1", "call_1", "\"cats\"}"),
            BuildFunctionCallDoneEvent("fc_1", "call_1", "search", "{\"q\":\"cats\"}"),
            BuildDoneEvent("completed", inputTokens: 10, outputTokens: 20));
        var (provider, _) = CreateProviderWithSseCapture(sseBody);

        var chunks = new List<StreamChunk>();
        await foreach (var chunk in provider.StreamAsync(SimpleRequest()))
            chunks.Add(chunk);

        var start = chunks.FirstOrDefault(c => c.Type == StreamChunkType.ToolUseStart);
        start.Should().NotBeNull();
        start!.ToolUse!.Id.Should().Be("call_1");
        start.ToolUse.Name.Should().Be("search");

        var deltas = chunks.Where(c => c.Type == StreamChunkType.ToolUseInputDelta).ToList();
        deltas.Should().HaveCount(2);
        deltas[0].ToolUse!.InputDelta.Should().Be("{\"q\":");
        deltas[1].ToolUse!.InputDelta.Should().Be("\"cats\"}");

        var complete = chunks.FirstOrDefault(c => c.Type == StreamChunkType.ToolUseComplete);
        complete.Should().NotBeNull();
        complete!.ToolUse!.Id.Should().Be("call_1");
        complete.ToolUse.Name.Should().Be("search");
        complete.ToolUse.Input.Should().NotBeNull();
    }

    [Fact]
    public async Task StreamAsync_Done_YieldsMessageStopWithUsage()
    {
        var sseBody = BuildSseBody(
            BuildTextDeltaEvent("Done"),
            BuildDoneEvent("completed", inputTokens: 15, outputTokens: 7));
        var (provider, _) = CreateProviderWithSseCapture(sseBody);

        var chunks = new List<StreamChunk>();
        await foreach (var chunk in provider.StreamAsync(SimpleRequest()))
            chunks.Add(chunk);

        var stop = chunks.FirstOrDefault(c => c.Type == StreamChunkType.MessageStop);
        stop.Should().NotBeNull();
        stop!.StopReason.Should().Be(ModelStopReason.EndTurn);
        stop.Usage!.InputTokens.Should().Be(15);
        stop.Usage.OutputTokens.Should().Be(7);
    }

    [Fact]
    public async Task StreamAsync_DoneWithFunctionCallOutput_StopReasonIsToolUse()
    {
        var sseBody = BuildSseBody(
            BuildFunctionCallAddedEvent("fc_2", "call_2", "my_tool"),
            BuildFunctionCallDoneEvent("fc_2", "call_2", "my_tool", "{}"),
            BuildDoneEvent("completed", inputTokens: 5, outputTokens: 5,
                outputItems: """[{"type":"function_call","id":"fc_2","call_id":"call_2","name":"my_tool","arguments":"{}"}]"""));
        var (provider, _) = CreateProviderWithSseCapture(sseBody);

        var chunks = new List<StreamChunk>();
        await foreach (var chunk in provider.StreamAsync(SimpleRequest()))
            chunks.Add(chunk);

        var stop = chunks.FirstOrDefault(c => c.Type == StreamChunkType.MessageStop);
        stop.Should().NotBeNull();
        stop!.StopReason.Should().Be(ModelStopReason.ToolUse);
    }

    // =========================================================================
    // 4. BuildInputItems unit tests
    // =========================================================================

    [Fact]
    public void BuildInputItems_SystemMessageIsSkipped()
    {
        var messages = new List<Message>
        {
            Message.System("instructions"),
            Message.User("Hello")
        };

        var items = OpenAIResponsesProvider.BuildInputItems(messages).ToList();

        items.Should().ContainSingle();
        items[0].TryGetPropertyValue("role", out var role).Should().BeTrue();
        role!.GetValue<string>().Should().Be("user");
    }

    [Fact]
    public void BuildInputItems_ToolResultOnly_ProducesTopLevelFunctionCallOutput()
    {
        var messages = new List<Message>
        {
            new()
            {
                Role = MessageRole.User,
                Content = [new ToolResultContent { ToolUseId = "call_99", Content = "ok" }]
            }
        };

        var items = OpenAIResponsesProvider.BuildInputItems(messages).ToList();

        items.Should().ContainSingle();
        items[0].TryGetPropertyValue("type", out var type).Should().BeTrue();
        type!.GetValue<string>().Should().Be("function_call_output");
        items[0].TryGetPropertyValue("call_id", out var callId).Should().BeTrue();
        callId!.GetValue<string>().Should().Be("call_99");
        items[0].TryGetPropertyValue("output", out var output).Should().BeTrue();
        output!.GetValue<string>().Should().Be("ok");
    }

    // =========================================================================
    // SSE builder helpers
    // =========================================================================

    private static string BuildSseBody(params string[] events) =>
        string.Join("\n", events) + "\n";

    private static string BuildTextDeltaEvent(string delta) =>
        "event: response.output_text.delta\n" +
        $"data: {{\"type\":\"response.output_text.delta\",\"delta\":\"{delta}\"}}\n\n";

    private static string BuildFunctionCallAddedEvent(string itemId, string callId, string name) =>
        "event: response.output_item.added\n" +
        $"data: {{\"type\":\"response.output_item.added\",\"output_index\":0," +
        $"\"item\":{{\"type\":\"function_call\",\"id\":\"{itemId}\",\"call_id\":\"{callId}\"," +
        $"\"name\":\"{name}\",\"arguments\":\"\"}}}}\n\n";

    private static string BuildFunctionCallDeltaEvent(string itemId, string callId, string delta)
    {
        var escapedDelta = delta.Replace("\\", "\\\\").Replace("\"", "\\\"");
        return "event: response.function_call_arguments.delta\n" +
               $"data: {{\"type\":\"response.function_call_arguments.delta\"," +
               $"\"item_id\":\"{itemId}\",\"call_id\":\"{callId}\",\"delta\":\"{escapedDelta}\"}}\n\n";
    }

    private static string BuildFunctionCallDoneEvent(string itemId, string callId, string name, string arguments)
    {
        var escapedArgs = arguments.Replace("\\", "\\\\").Replace("\"", "\\\"");
        return "event: response.output_item.done\n" +
               $"data: {{\"type\":\"response.output_item.done\",\"output_index\":0," +
               $"\"item\":{{\"type\":\"function_call\",\"id\":\"{itemId}\",\"call_id\":\"{callId}\"," +
               $"\"name\":\"{name}\",\"arguments\":\"{escapedArgs}\"}}}}\n\n";
    }

    private static string BuildDoneEvent(
        string status,
        int inputTokens = 0,
        int outputTokens = 0,
        string? outputItems = null)
    {
        var output = outputItems ?? "[]";
        return "event: response.done\n" +
               $"data: {{\"type\":\"response.done\",\"response\":{{\"id\":\"resp_test\"," +
               $"\"status\":\"{status}\",\"output\":{output}," +
               $"\"usage\":{{\"input_tokens\":{inputTokens},\"output_tokens\":{outputTokens}," +
               $"\"total_tokens\":{inputTokens + outputTokens}}}}}}}\n\n";
    }

    // =========================================================================
    // Test helpers
    // =========================================================================

    private sealed class CapturingHttpMessageHandler(List<string> bodies, string responseBody)
        : HttpMessageHandler
    {
        protected override async Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request, CancellationToken cancellationToken)
        {
            if (request.Content is not null)
                bodies.Add(await request.Content.ReadAsStringAsync(cancellationToken));

            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(responseBody, Encoding.UTF8, "application/json")
            };
        }
    }
}
