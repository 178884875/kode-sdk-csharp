using FluentAssertions;
using Kode.Agent.Sdk.Core.Abstractions;
using Kode.Agent.Sdk.Core.Context;
using Kode.Agent.Sdk.Core.Types;
using Moq;
using Xunit;

namespace Kode.Agent.Tests.Unit.Context;

/// <summary>
/// Unit tests for ContextManager covering:
///   P1-A  tool_use/tool_result pair atomic protection
///   P1-B  Analyze() systemPromptTokens parameter
///   P4    MergeSummaryStackAsync propagates CoreMemoryUpdate
///   P5    CompressAsync systemPromptTokens parameter
/// </summary>
public sealed class ContextManagerTests
{
    // ── Helpers ───────────────────────────────────────────────────────────────

    private static ContextManager CreateManager(
        ContextManagerOptions? options = null,
        IContextSummarizer? summarizer = null)
    {
        var storeMock = new Mock<IAgentStore>();
        storeMock.Setup(s => s.SaveHistoryWindowAsync(It.IsAny<string>(), It.IsAny<HistoryWindow>(), It.IsAny<CancellationToken>()))
                 .Returns(Task.CompletedTask);
        storeMock.Setup(s => s.SaveCompressionRecordAsync(It.IsAny<string>(), It.IsAny<CompressionRecord>(), It.IsAny<CancellationToken>()))
                 .Returns(Task.CompletedTask);

        return new ContextManager(storeMock.Object, "test-agent", options, summarizer);
    }

    private static Message UserMsg(string text) => Message.User(text);
    private static Message AssistantMsg(string text) => Message.Assistant(text);

    private static Message MsgWithToolUse(string toolUseId, string toolName = "fs_read") =>
        new Message
        {
            Role = MessageRole.Assistant,
            Content =
            [
                new TextContent { Text = "Calling tool" },
                new ToolUseContent { Id = toolUseId, Name = toolName, Input = System.Text.Json.JsonDocument.Parse("{}").RootElement }
            ]
        };

    private static Message MsgWithToolResult(string toolUseId) =>
        new Message
        {
            Role = MessageRole.User,
            Content =
            [
                new ToolResultContent { ToolUseId = toolUseId, Content = "result content" }
            ]
        };

    private static Message SummaryMsg(string text, int n = 1) =>
        Message.System($"<context-summary window=\"w{n}\">{text}</context-summary>");

    private static Message CoreMemoryMsg(string text) =>
        Message.System($"<core-memory>{text}</core-memory>");

    // ── P1-B: systemPromptTokens in Analyze() ────────────────────────────────

    [Fact]
    public void Analyze_WithoutSystemPromptTokens_DoesNotCountSystemPrompt()
    {
        var manager = CreateManager(new ContextManagerOptions { MaxTokens = 100 });
        // Messages alone are small
        var messages = new List<Message> { UserMsg("hi") };

        var usage = manager.Analyze(messages, systemPromptTokens: 0);

        usage.ShouldCompress.Should().BeFalse();
    }

    [Fact]
    public void Analyze_SystemPromptTokensAddedToTotal()
    {
        var manager = CreateManager(new ContextManagerOptions { MaxTokens = 100 });
        var messages = new List<Message> { UserMsg("hi") }; // ~10 tokens

        // Without system prompt: below threshold
        var withoutSys = manager.Analyze(messages, systemPromptTokens: 0);
        withoutSys.ShouldCompress.Should().BeFalse();

        // With large system prompt pushing over threshold
        var withSys = manager.Analyze(messages, systemPromptTokens: 200);
        withSys.ShouldCompress.Should().BeTrue();
    }

    [Fact]
    public void Analyze_TotalTokensIncludesSystemPromptTokens()
    {
        var manager = CreateManager();
        var messages = new List<Message> { UserMsg("hello") };

        var withoutSys = manager.Analyze(messages, systemPromptTokens: 0);
        var withSys = manager.Analyze(messages, systemPromptTokens: 500);

        withSys.TotalTokens.Should().Be(withoutSys.TotalTokens + 500);
    }

    [Fact]
    public void EstimateSystemPromptTokens_EmptyOrNull_ReturnsZero()
    {
        ContextManager.EstimateSystemPromptTokens(null).Should().Be(0);
        ContextManager.EstimateSystemPromptTokens("").Should().Be(0);
    }

    [Fact]
    public void EstimateSystemPromptTokens_EnglishText_ReturnsPositiveCount()
    {
        var tokens = ContextManager.EstimateSystemPromptTokens("You are a helpful assistant.");
        tokens.Should().BeGreaterThan(0);
    }

    [Fact]
    public void EstimateSystemPromptTokens_CjkText_HigherThanEquivalentEnglish()
    {
        // CJK should count as ~1.5 tokens/char vs 0.25 for ASCII
        var cjk = ContextManager.EstimateSystemPromptTokens("你是一个有用的助手");   // 9 CJK chars
        var ascii = ContextManager.EstimateSystemPromptTokens("You are helpful!!"); // ~same length
        cjk.Should().BeGreaterThan(ascii);
    }

    // ── P1-A: tool_use / tool_result pair protection ──────────────────────────

    [Fact]
    public async Task Compress_RemovingToolUse_AlsoRemovesMatchingToolResult()
    {
        // Arrange: 3 old low-score messages + tool pair + recent messages
        // The tool pair is old enough that it would be removed by recency alone.
        var toolUseId = "tu-001";

        var messages = new List<Message>
        {
            UserMsg("old message 1"),
            UserMsg("old message 2"),
            UserMsg("old message 3"),
            MsgWithToolUse(toolUseId, "fs_read"),    // tool_use — old, low score
            MsgWithToolResult(toolUseId),             // tool_result — should be removed atomically
            UserMsg("recent message A"),
            UserMsg("recent message B"),
        };

        // Tight budget — forces removal of old messages
        var options = new ContextManagerOptions
        {
            MaxTokens = 1,    // always compress
            CompressToTokens = 50,
            MinRecentMessages = 2,
        };

        var summarizer = new StaticContextSummarizer();
        var manager = CreateManager(options, summarizer);

        // Act
        var result = await manager.CompressAsync(messages, [], cancellationToken: CancellationToken.None);

        // Assert: if tool_use was removed, its tool_result must also be removed
        result.Should().NotBeNull();
        var removedIds = result!.RemovedMessages
            .SelectMany(m => m.Content.OfType<ToolUseContent>().Select(t => t.Id)
                .Concat(m.Content.OfType<ToolResultContent>().Select(t => t.ToolUseId)))
            .ToHashSet();

        var retainedIds = result.RetainedMessages
            .SelectMany(m => m.Content.OfType<ToolUseContent>().Select(t => t.Id)
                .Concat(m.Content.OfType<ToolResultContent>().Select(t => t.ToolUseId)))
            .ToHashSet();

        // tool_use and tool_result must be on the same side (both removed or both retained)
        if (removedIds.Contains(toolUseId))
            removedIds.Should().Contain(toolUseId, "tool_use and tool_result must be removed atomically");
        else
            retainedIds.Should().Contain(toolUseId, "tool_use and tool_result must be retained atomically");
    }

    [Fact]
    public async Task Compress_ProtectedToolResult_KeepsToolUseToo()
    {
        var toolUseId = "tu-protected";

        // tool_use is old (index 0), tool_result is among the last 2 (protected)
        var messages = new List<Message>
        {
            MsgWithToolUse(toolUseId, "fs_write"),  // index 0 — old
            UserMsg("filler 1"),
            UserMsg("filler 2"),
            UserMsg("filler 3"),
            UserMsg("filler 4"),
            MsgWithToolResult(toolUseId),            // index 5 — protected (MinRecentMessages=2)
            UserMsg("very recent"),                  // index 6 — protected
        };

        var options = new ContextManagerOptions
        {
            MaxTokens = 1,
            CompressToTokens = 30,
            MinRecentMessages = 2,
        };

        var manager = CreateManager(options, new StaticContextSummarizer());
        var result = await manager.CompressAsync(messages, [], cancellationToken: CancellationToken.None);

        result.Should().NotBeNull();

        // tool_result is protected → tool_use must NOT be removed either
        var removedHasToolUse = result!.RemovedMessages
            .Any(m => m.Content.OfType<ToolUseContent>().Any(t => t.Id == toolUseId));

        removedHasToolUse.Should().BeFalse(
            "tool_use should not be removed when its paired tool_result is in the protected window");
    }

    // ── P4: MergeSummaryStack propagates CoreMemoryUpdate ────────────────────

    [Fact]
    public async Task Compress_WhenSummaryStackMerges_CoreMemoryIsUpdated()
    {
        // Arrange: pre-existing summary stack at MaxSummaryDepth
        const int depth = 3;
        var options = new ContextManagerOptions
        {
            MaxTokens = 1,
            CompressToTokens = 5000,
            EnableCoreMemory = true,
            MaxSummaryDepth = depth,
        };

        var coreMemoryContent = "## Current Task\nTest task";
        var summarizer = new CapturingSummarizer(
            summary: "merged summary",
            coreMemoryUpdate: coreMemoryContent);

        var messages = new List<Message> { UserMsg("trigger compression") };

        // Add existing summaries up to the depth limit
        for (var i = 1; i <= depth; i++)
            messages.Insert(0, SummaryMsg($"summary {i}", i));

        var manager = CreateManager(options, summarizer);

        // Act
        var result = await manager.CompressAsync(messages, [], cancellationToken: CancellationToken.None);

        // Assert: a core-memory message should appear in retained messages
        result.Should().NotBeNull();
        var hasCoreMemory = result!.RetainedMessages
            .Any(m => m.Role == MessageRole.System
                   && string.Join("", m.Content.OfType<TextContent>().Select(t => t.Text))
                        .Contains("<core-memory", StringComparison.Ordinal));

        hasCoreMemory.Should().BeTrue("core-memory should be updated when merge produces a CoreMemoryUpdate");
    }

    // ── P5: CompressAsync systemPromptTokens ─────────────────────────────────

    [Fact]
    public async Task CompressAsync_WithSystemPromptTokens_DoesNotCompressWhenBelowThreshold()
    {
        // Arrange: messages alone below threshold, system prompt tokens push it over
        var options = new ContextManagerOptions
        {
            MaxTokens = 200,
            CompressToTokens = 100,
        };
        var manager = CreateManager(options, new StaticContextSummarizer());
        var messages = new List<Message> { UserMsg("short") }; // tiny — well below 200 tokens

        // Act: without system prompt tokens → should NOT compress
        var result = await manager.CompressAsync(messages, [], systemPromptTokens: 0);
        result.Should().BeNull("messages alone are below MaxTokens");
    }

    [Fact]
    public async Task CompressAsync_WithSystemPromptTokens_CompressesWhenOverThreshold()
    {
        var options = new ContextManagerOptions
        {
            MaxTokens = 50,
            CompressToTokens = 20,
        };
        var manager = CreateManager(options, new StaticContextSummarizer());
        var messages = new List<Message> { UserMsg("short") }; // ~10 tokens alone

        // With system prompt tokens pushing total over 50
        var result = await manager.CompressAsync(messages, [], systemPromptTokens: 100);
        result.Should().NotBeNull("system prompt tokens should push total over MaxTokens");
    }

    // ── Helper summarizer that captures calls ─────────────────────────────────

    private sealed class CapturingSummarizer : IContextSummarizer
    {
        private readonly string _summary;
        private readonly string? _coreMemoryUpdate;

        public CapturingSummarizer(string summary, string? coreMemoryUpdate = null)
        {
            _summary = summary;
            _coreMemoryUpdate = coreMemoryUpdate;
        }

        public int CallCount { get; private set; }

        public Task<SummaryResult> SummarizeAsync(
            IReadOnlyList<Message> removedMessages,
            ContextManagerOptions options,
            CancellationToken cancellationToken = default)
        {
            CallCount++;
            return Task.FromResult(new SummaryResult(_summary, _coreMemoryUpdate));
        }
    }
}
