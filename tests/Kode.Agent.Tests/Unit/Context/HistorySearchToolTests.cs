using System.Text.Json;
using FluentAssertions;
using Kode.Agent.Sdk.Core.Abstractions;
using Kode.Agent.Sdk.Core.Context;
using Kode.Agent.Sdk.Core.Types;
using Kode.Agent.Tools.Builtin.History;
using Moq;
using Xunit;
// ReSharper disable MemberCanBeMadeStatic.Local

namespace Kode.Agent.Tests.Unit.Context;

public sealed class HistorySearchToolTests
{
    private static readonly HistorySearchTool Tool = new HistorySearchTool();

    // ── Helpers ───────────────────────────────────────────────────────────────

    private static ToolContext ContextWith(IReadOnlyList<HistoryWindow> windows)
    {
        var agentMock = new Mock<IAgent>();
        agentMock
            .Setup(a => a.GetHistoryWindowsAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(windows);

        return new ToolContext
        {
            AgentId = "test-agent",
            CallId = "test-call",
            Sandbox = new Mock<ISandbox>().Object,
            Agent = agentMock.Object,
        };
    }

    private static ToolContext ContextWithNoAgent() => new ToolContext
    {
        AgentId = "test-agent",
        CallId = "test-call",
        Sandbox = new Mock<ISandbox>().Object,
        Agent = null,
    };

    private static HistoryWindow WindowWith(string id, long timestamp, params string[] userTexts)
    {
        var messages = userTexts.Select(Message.User).ToList<Message>();
        return new HistoryWindow
        {
            Id = id,
            Messages = messages,
            Stats = new HistoryWindowStats(messages.Count, 100),
            Timestamp = timestamp,
        };
    }

    private static HistoryWindow WindowWithSummary(string id, long timestamp, string summaryText)
    {
        var msg = Message.System($"<context-summary window=\"{id}\">{summaryText}</context-summary>");
        return new HistoryWindow
        {
            Id = id,
            Messages = [msg],
            Stats = new HistoryWindowStats(1, 100),
            Timestamp = timestamp,
        };
    }

    private static HistorySearchArgs Args(string query, int limit = 5) =>
        new() { Query = query, Limit = limit };

    private static string Json(object? value) =>
        value == null ? "" : JsonSerializer.Serialize(value);

    // ── Error cases ───────────────────────────────────────────────────────────

    [Fact]
    public async Task Execute_EmptyQuery_ReturnsFailure()
    {
        var context = ContextWith([]);
        var result = await Tool.ExecuteAsync(Args(""), context);

        result.Success.Should().BeFalse();
    }

    [Fact]
    public async Task Execute_NoAgent_ReturnsFailure()
    {
        var result = await Tool.ExecuteAsync(Args("test"), ContextWithNoAgent());

        result.Success.Should().BeFalse();
    }

    // ── Empty history ─────────────────────────────────────────────────────────

    [Fact]
    public async Task Execute_EmptyHistory_ReturnsNoteAboutNoHistory()
    {
        var context = ContextWith([]);
        var result = await Tool.ExecuteAsync(Args("database"), context);

        result.Success.Should().BeTrue();
        var text = Json(result.Value);
        text.Should().Contain("No compressed history");
    }

    // ── No matches ───────────────────────────────────────────────────────────

    [Fact]
    public async Task Execute_NoMatchingContent_ReturnsNoMatchesNote()
    {
        var context = ContextWith([WindowWith("w1", 1000, "completely unrelated content here")]);
        var result = await Tool.ExecuteAsync(Args("xyzzy-nonexistent-term"), context);

        result.Success.Should().BeTrue();
        var text = Json(result.Value);
        text.Should().Contain("No matches");
    }

    // ── Matching ──────────────────────────────────────────────────────────────

    [Fact]
    public async Task Execute_MatchingContent_ReturnsResults()
    {
        var windows = new List<HistoryWindow>
        {
            WindowWith("w1", 1000, "the database schema was changed to add an index"),
            WindowWith("w2", 2000, "unrelated message about something else entirely"),
        };

        var context = ContextWith(windows);
        var result = await Tool.ExecuteAsync(Args("database schema"), context);

        result.Success.Should().BeTrue();
        var text = Json(result.Value);
        text.Should().Contain("w1", because: "window w1 contains the query terms");
    }

    [Fact]
    public async Task Execute_SummaryWindowMatchesHigherThanRegular()
    {
        var windows = new List<HistoryWindow>
        {
            WindowWith("regular", 1000, "database index mentioned once"),
            WindowWithSummary("summarized", 2000, "database index optimization: added composite index on user_id and created_at"),
        };

        var context = ContextWith(windows);
        var result = await Tool.ExecuteAsync(Args("database index"), context);

        result.Success.Should().BeTrue();
        var text = Json(result.Value);
        text.Should().Contain("summarized");
    }

    [Fact]
    public async Task Execute_RespectsLimit()
    {
        var windows = Enumerable.Range(1, 10)
            .Select(i => WindowWith($"w{i}", i * 1000L, "target keyword content match"))
            .ToList<HistoryWindow>();

        var context = ContextWith(windows);
        var result = await Tool.ExecuteAsync(Args("target keyword", limit: 3), context);

        result.Success.Should().BeTrue();
        var text = Json(result.Value);
        var matchCount = Enumerable.Range(1, 10).Count(i => text.Contains($"w{i}"));
        matchCount.Should().BeLessThanOrEqualTo(3);
    }

    [Fact]
    public async Task Execute_LimitClampedToMax20()
    {
        var windows = Enumerable.Range(1, 25)
            .Select(i => WindowWith($"w{i}", i * 1000L, "query match"))
            .ToList<HistoryWindow>();

        var context = ContextWith(windows);
        var result = await Tool.ExecuteAsync(Args("query match", limit: 100), context);

        result.Success.Should().BeTrue();
        var text = Json(result.Value);
        var matchCount = Enumerable.Range(1, 25).Count(i => text.Contains($"w{i}"));
        matchCount.Should().BeLessThanOrEqualTo(20);
    }

    [Fact]
    public async Task Execute_ResultContainsTotalWindowsCount()
    {
        var windows = new List<HistoryWindow>
        {
            WindowWith("w1", 1000, "match this query"),
            WindowWith("w2", 2000, "match this query"),
            WindowWith("w3", 3000, "unrelated"),
        };

        var context = ContextWith(windows);
        var result = await Tool.ExecuteAsync(Args("match this query"), context);

        var text = Json(result.Value);
        text.Should().Contain("totalWindows");
        text.Should().Contain("3", because: "there are 3 windows total");
    }

    [Fact]
    public async Task Execute_CjkQuery_MatchesCjkContent()
    {
        var windows = new List<HistoryWindow>
        {
            WindowWith("w1", 1000, "讨论了数据库优化方案，最终决定添加索引"),
        };

        var context = ContextWith(windows);
        var result = await Tool.ExecuteAsync(Args("数据库"), context);

        result.Success.Should().BeTrue();
        var text = Json(result.Value);
        text.Should().Contain("w1");
    }

    // ── Tool metadata ─────────────────────────────────────────────────────────

    [Fact]
    public void Tool_Name_IsHistorySearch()
    {
        Tool.Name.Should().Be("history_search");
    }

    [Fact]
    public void Tool_IsReadOnly()
    {
        Tool.Attributes.ReadOnly.Should().BeTrue();
    }

    [Fact]
    public void Tool_DoesNotRequireApproval()
    {
        Tool.Attributes.RequiresApproval.Should().BeFalse();
    }
}

