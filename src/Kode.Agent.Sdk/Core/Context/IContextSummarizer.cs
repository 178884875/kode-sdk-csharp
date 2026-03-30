namespace Kode.Agent.Sdk.Core.Context;

/// <summary>
/// Result of a compression summarization.
/// </summary>
/// <param name="Summary">Semantic summary of the removed messages.</param>
/// <param name="CoreMemoryUpdate">
/// Optional updated core-memory block content (task objective, modified files, key decisions).
/// Null means the existing core-memory block should be preserved unchanged.
/// </param>
public record SummaryResult(string Summary, string? CoreMemoryUpdate = null);

/// <summary>
/// Strategy for generating a compression summary from removed messages.
/// </summary>
public interface IContextSummarizer
{
    Task<SummaryResult> SummarizeAsync(
        IReadOnlyList<Message> removedMessages,
        ContextManagerOptions options,
        CancellationToken cancellationToken = default);
}
