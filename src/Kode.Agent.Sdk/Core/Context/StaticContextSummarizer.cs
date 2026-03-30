namespace Kode.Agent.Sdk.Core.Context;

/// <summary>
/// Fallback summarizer: produces a static stats-based summary without LLM.
/// No core-memory update is produced.
/// </summary>
public class StaticContextSummarizer : IContextSummarizer
{
    public Task<SummaryResult> SummarizeAsync(
        IReadOnlyList<Message> removedMessages,
        ContextManagerOptions options,
        CancellationToken cancellationToken = default)
    {
        var lines = new List<string>();
        lines.Add($"Compressed {removedMessages.Count} messages from conversation history.");

        var userMessages = 0;
        var assistantMessages = 0;
        var toolCalls = 0;

        foreach (var msg in removedMessages)
        {
            if (msg.Role == MessageRole.User) userMessages++;
            else if (msg.Role == MessageRole.Assistant) assistantMessages++;

            toolCalls += msg.Content.OfType<ToolUseContent>().Count();
        }

        lines.Add($"Summary: {userMessages} user messages, {assistantMessages} assistant responses, {toolCalls} tool calls.");

        var firstUser = removedMessages.FirstOrDefault(m => m.Role == MessageRole.User);
        var lastUser = removedMessages.LastOrDefault(m => m.Role == MessageRole.User);

        if (firstUser != null)
        {
            var text = string.Join(" ", firstUser.Content.OfType<TextContent>().Select(t => t.Text));
            if (text.Length > 0)
                lines.Add($"Initial topic: {Preview(text, 200)}");
        }

        if (lastUser != null && lastUser != firstUser)
        {
            var text = string.Join(" ", lastUser.Content.OfType<TextContent>().Select(t => t.Text));
            if (text.Length > 0)
                lines.Add($"Last topic: {Preview(text, 200)}");
        }

        return Task.FromResult(new SummaryResult(string.Join("\n", lines)));
    }

    private static string Preview(string text, int limit) =>
        text.Length > limit ? text[..limit] + "…" : text;
}
