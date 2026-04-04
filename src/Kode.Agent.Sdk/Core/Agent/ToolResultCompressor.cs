using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Logging;

namespace Kode.Agent.Sdk.Core.Agent;

/// <summary>
/// Configuration for large tool result compression.
/// </summary>
public record ToolResultCompressionOptions
{
    /// <summary>
    /// Whether tool result compression is enabled. Default: false (opt-in).
    /// </summary>
    public bool Enabled { get; init; } = false;

    /// <summary>
    /// Result size in bytes that triggers compression. Default: 50 000 (~50 KB).
    /// </summary>
    public int ThresholdBytes { get; init; } = 50_000;

    /// <summary>
    /// Tool names whose results are safe to compress.
    /// Null means use the built-in defaults: bash_run, bash_logs, and any mcp__* tool.
    /// Tools that produce content the agent must reference verbatim for follow-up
    /// operations (e.g. fs_read → fs_edit) should NOT be listed here.
    /// </summary>
    public IReadOnlyList<string>? CompressibleTools { get; init; }

    /// <summary>
    /// Model used for compression. Null = agent's primary model.
    /// </summary>
    public string? CompressionModel { get; init; }
}

/// <summary>
/// Compresses oversized tool results via LLM summarization so they don't bloat the
/// context window. Leaves small results and non-compressible tools untouched.
/// Falls back to byte-level truncation when the LLM call fails.
/// </summary>
public interface IToolResultCompressor
{
    /// <summary>
    /// Returns a (possibly compressed) replacement for <paramref name="result"/>.
    /// The returned result is always structurally valid.
    /// </summary>
    Task<ToolResult> CompressIfNeededAsync(
        string toolName,
        ToolResult result,
        IReadOnlyList<Message> recentMessages,
        ToolResultCompressionOptions options,
        CancellationToken cancellationToken = default);
}

/// <summary>
/// LLM-backed tool result compressor.
/// </summary>
public sealed class LlmToolResultCompressor : IToolResultCompressor
{
    // Default tools whose results may be safely summarised.
    // File-read tools are intentionally excluded because the agent uses their
    // exact text for follow-up edit operations.
    private static readonly HashSet<string> DefaultCompressibleTools = new(StringComparer.OrdinalIgnoreCase)
    {
        "bash_run",
        "bash_logs",
    };

    // Input fed to the LLM is capped to keep the compression call cheap.
    private const int MaxInputChars = 12_000;
    private const int MaxOutputTokens = 600;

    private readonly IModelProvider _modelProvider;
    private readonly string? _primaryModel;
    private readonly ILogger<LlmToolResultCompressor>? _logger;

    public LlmToolResultCompressor(
        IModelProvider modelProvider,
        string? primaryModel = null,
        ILogger<LlmToolResultCompressor>? logger = null)
    {
        _modelProvider = modelProvider;
        _primaryModel = primaryModel;
        _logger = logger;
    }

    public async Task<ToolResult> CompressIfNeededAsync(
        string toolName,
        ToolResult result,
        IReadOnlyList<Message> recentMessages,
        ToolResultCompressionOptions options,
        CancellationToken cancellationToken = default)
    {
        if (!result.Success)
            return result;

        var serialized = Serialize(result.Value);
        if (serialized.Length <= options.ThresholdBytes)
            return result;

        if (!IsCompressible(toolName, options))
            return result;

        var originalBytes = serialized.Length;
        _logger?.LogDebug(
            "Compressing tool result for '{Tool}' ({Bytes:N0} bytes > threshold {Threshold:N0})",
            toolName, originalBytes, options.ThresholdBytes);

        try
        {
            var summary = await SummarizeAsync(toolName, serialized, recentMessages, options, cancellationToken);
            var compressed = ToolResult.Ok(new
            {
                compressed = true,
                originalBytes,
                tool = toolName,
                summary
            });
            _logger?.LogDebug("Compressed '{Tool}' result from {Original:N0} to {Final:N0} bytes",
                toolName, originalBytes, Serialize(compressed.Value).Length);
            return compressed;
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger?.LogWarning(ex, "LLM compression failed for '{Tool}', falling back to truncation", toolName);
            return TruncateFallback(toolName, serialized, originalBytes, options.ThresholdBytes);
        }
    }

    private async Task<string> SummarizeAsync(
        string toolName,
        string rawContent,
        IReadOnlyList<Message> recentMessages,
        ToolResultCompressionOptions options,
        CancellationToken cancellationToken)
    {
        var model = options.CompressionModel ?? _primaryModel ?? "claude-haiku-4-5-20251001";

        var systemPrompt =
            """
            You are a tool-result summarizer for an AI agent. Your job is to condense a large tool
            output into a concise summary that preserves everything the agent needs to continue its task.

            Rules:
            - Keep the summary under 300 words.
            - Preserve key facts: numbers, file paths, error messages, status codes, important names.
            - If the output is structured (JSON/CSV/table), describe the schema and highlight notable rows.
            - If the output is command output or logs, capture the overall outcome and any warnings/errors.
            - Do NOT add commentary or opinions; be neutral and factual.
            - Output plain text only (no markdown headers).
            """;

        var userContent = BuildUserPrompt(toolName, rawContent, recentMessages);

        var request = new ModelRequest
        {
            Model = model,
            SystemPrompt = systemPrompt,
            Messages = [Message.User(userContent)],
            MaxTokens = MaxOutputTokens,
        };

        var response = await _modelProvider.CompleteAsync(request, cancellationToken);
        return response.Content.OfType<TextContent>().FirstOrDefault()?.Text?.Trim()
            ?? "(no summary produced)";
    }

    private static string BuildUserPrompt(
        string toolName,
        string rawContent,
        IReadOnlyList<Message> recentMessages)
    {
        var sb = new StringBuilder();

        // Add task context from last 3 user messages
        var userMsgs = recentMessages
            .Where(m => m.Role == MessageRole.User)
            .TakeLast(3)
            .ToList();

        if (userMsgs.Count > 0)
        {
            sb.AppendLine("## Current task context (last user messages)");
            foreach (var msg in userMsgs)
            {
                var text = string.Join(" ", msg.Content.OfType<TextContent>().Select(t => t.Text));
                if (!string.IsNullOrWhiteSpace(text))
                    sb.AppendLine(Preview(text, 300));
            }
            sb.AppendLine();
        }

        sb.AppendLine($"## Tool: {toolName}");
        sb.AppendLine($"## Raw output ({rawContent.Length:N0} chars — truncated to {MaxInputChars:N0} for this prompt)");
        sb.AppendLine();
        sb.Append(rawContent.Length > MaxInputChars ? rawContent[..MaxInputChars] + "\n…[truncated]" : rawContent);

        return sb.ToString();
    }

    private static ToolResult TruncateFallback(
        string toolName, string serialized, int originalBytes, int threshold)
    {
        var kept = serialized[..Math.Min(serialized.Length, threshold)];
        return ToolResult.Ok(new
        {
            compressed = true,
            truncated = true,
            originalBytes,
            tool = toolName,
            summary = $"[Result truncated: {originalBytes:N0} bytes exceeded threshold. First {threshold:N0} bytes:]",
            partial = kept
        });
    }

    private static bool IsCompressible(string toolName, ToolResultCompressionOptions options)
    {
        if (options.CompressibleTools != null)
            return options.CompressibleTools.Contains(toolName, StringComparer.OrdinalIgnoreCase);

        // Default: bash tools + any MCP tool
        return DefaultCompressibleTools.Contains(toolName)
            || toolName.StartsWith("mcp__", StringComparison.OrdinalIgnoreCase);
    }

    private static string Serialize(object? value)
    {
        if (value is null) return "";
        if (value is string s) return s;
        try { return JsonSerializer.Serialize(value, new JsonSerializerOptions(JsonSerializerDefaults.Web)); }
        catch { return value.ToString() ?? ""; }
    }

    private static string Preview(string text, int limit) =>
        text.Length > limit ? text[..limit] + "…" : text;
}
