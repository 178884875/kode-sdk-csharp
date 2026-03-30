using System.Text.Json.Serialization;
using Kode.Agent.Sdk.Core.Abstractions;
using Kode.Agent.Sdk.Core.Context;
using Kode.Agent.Sdk.Core.Types;
using Kode.Agent.Sdk.Tools;

namespace Kode.Agent.Tools.Builtin.History;

/// <summary>
/// Arguments for the history_search tool.
/// </summary>
public sealed class HistorySearchArgs
{
    /// <summary>Search query (keywords or phrase).</summary>
    [JsonPropertyName("query")]
    public string Query { get; set; } = "";

    /// <summary>Maximum number of results to return (default: 5, max: 20).</summary>
    [JsonPropertyName("limit")]
    public int Limit { get; set; } = 5;
}

/// <summary>
/// Searches compressed conversation history using BM25 ranking.
///
/// Each result corresponds to a history window — a snapshot saved at the time of a
/// context compression. The tool returns the most relevant snippets with their
/// approximate timestamps, allowing the agent to recall important context that was
/// compressed out of the active window.
///
/// Useful when the user references something from a past session or when the agent
/// needs to verify a prior decision, preference, or outcome.
/// </summary>
[Tool("history_search")]
public class HistorySearchTool : ToolBase<HistorySearchArgs>
{
    public override string Name => "history_search";

    public override string Description =>
        "Search compressed conversation history for relevant context. " +
        "Use when the user references something from earlier in the session that is no longer in the active context window. " +
        "Returns the most relevant historical snippets ranked by BM25 relevance.";

    public override object InputSchema => JsonSchemaBuilder.BuildSchema<HistorySearchArgs>();

    public override ToolAttributes Attributes => new()
    {
        ReadOnly = true,
        RequiresApproval = false,
    };

    public override ValueTask<string?> GetPromptAsync(ToolContext context)
    {
        return ValueTask.FromResult<string?>(
            "Use history_search when the user says something like \"remember when we discussed X\" or " +
            "\"what did we decide about Y\" and the relevant content is no longer in the active context. " +
            "Provide a concise keyword query (2-5 words) for best results.");
    }

    protected override async Task<ToolResult> ExecuteAsync(
        HistorySearchArgs args,
        ToolContext context,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(args.Query))
            return ToolResult.Fail("Query must not be empty.");

        if (context.Agent is not IAgent agent)
            return ToolResult.Fail("history_search is not available outside an agent session.");

        var limit = Math.Clamp(args.Limit, 1, 20);

        IReadOnlyList<HistoryWindow> windows;
        try
        {
            windows = await agent.GetHistoryWindowsAsync(cancellationToken);
        }
        catch (Exception ex)
        {
            return ToolResult.Fail($"Failed to load history: {ex.Message}");
        }

        if (windows.Count == 0)
            return ToolResult.Ok(new { results = Array.Empty<object>(), note = "No compressed history found for this session." });

        var index = BuildIndex(windows);
        var results = index.Search(args.Query, limit);

        if (results.Count == 0)
            return ToolResult.Ok(new { results = Array.Empty<object>(), note = $"No matches found for query: {args.Query}" });

        var output = results.Select(r => new
        {
            windowId = r.DocumentId,
            score = Math.Round(r.Score, 3),
            timestamp = DateTimeOffset.FromUnixTimeMilliseconds(r.Timestamp).ToString("yyyy-MM-dd HH:mm UTC"),
            snippet = r.Snippet,
        }).ToArray();

        return ToolResult.Ok(new { query = args.Query, totalWindows = windows.Count, results = output });
    }

    // ── Index construction ────────────────────────────────────────────────────

    private static BM25Index BuildIndex(IReadOnlyList<HistoryWindow> windows)
    {
        var index = new BM25Index();

        foreach (var window in windows)
        {
            var (text, snippet) = ExtractContent(window);
            if (!string.IsNullOrWhiteSpace(text))
                index.Add(window.Id, text, snippet, window.Timestamp);
        }

        return index;
    }

    /// <summary>
    /// Extracts searchable text and a human-readable snippet from a history window.
    /// Priority order for content:
    ///   1. context-summary messages (LLM-compressed, highest signal)
    ///   2. user messages (original intent)
    ///   3. assistant text (decisions and explanations)
    /// Tool results and system messages (other than summaries) are excluded to reduce noise.
    /// </summary>
    private static (string text, string snippet) ExtractContent(HistoryWindow window)
    {
        var sb = new System.Text.StringBuilder();
        var snippetBuilder = new System.Text.StringBuilder();
        const string summaryTag = "<context-summary";

        // Pass 1: summary messages (highest priority)
        foreach (var msg in window.Messages)
        {
            if (msg.Role != MessageRole.System) continue;
            var text = GetText(msg);
            if (!text.Contains(summaryTag, StringComparison.Ordinal)) continue;
            // Strip XML tags for indexing
            var inner = StripXmlTags(text);
            sb.Append(inner).Append(' ');
            if (snippetBuilder.Length < 300)
                snippetBuilder.Append(inner[..Math.Min(inner.Length, 300)]);
        }

        // Pass 2: user messages
        foreach (var msg in window.Messages)
        {
            if (msg.Role != MessageRole.User) continue;
            var text = GetText(msg);
            if (string.IsNullOrWhiteSpace(text)) continue;
            sb.Append(text).Append(' ');
            if (snippetBuilder.Length < 300)
                snippetBuilder.Append(text[..Math.Min(text.Length, 150)]).Append(' ');
        }

        // Pass 3: assistant text
        foreach (var msg in window.Messages)
        {
            if (msg.Role != MessageRole.Assistant) continue;
            var text = GetText(msg);
            if (string.IsNullOrWhiteSpace(text)) continue;
            sb.Append(text).Append(' ');
        }

        var snippetStr = snippetBuilder.Length > 0 ? snippetBuilder.ToString().Trim() : "";
        var snippet = snippetStr.Length > 0
            ? snippetStr[..Math.Min(snippetStr.Length, 400)]
            : "(no preview available)";

        return (sb.ToString(), snippet);
    }

    private static string GetText(Message msg) =>
        string.Join(" ", msg.Content.OfType<TextContent>().Select(t => t.Text));

    private static string StripXmlTags(string xml) =>
        System.Text.RegularExpressions.Regex.Replace(xml, "<[^>]+>", " ").Trim();
}
