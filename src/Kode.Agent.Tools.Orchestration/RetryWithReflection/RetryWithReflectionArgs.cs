using Kode.Agent.Sdk.Tools;

namespace Kode.Agent.Tools.Orchestration;

/// <summary>
/// Arguments for the <c>retry_with_reflection</c> tool.
/// </summary>
[GenerateToolSchema]
public class RetryWithReflectionArgs
{
    /// <summary>
    /// The task to attempt. On failure the sub-agent is retried with the error
    /// and a reflection prompt prepended to the original task.
    /// </summary>
    [ToolParameter(Description = "The task to attempt. Will be retried on failure with error context and a reflection prompt.")]
    public required string Task { get; init; }

    /// <summary>
    /// Maximum number of retry attempts after the initial failure.
    /// Total attempts = MaxRetries + 1. Clamped to [0, 5].
    /// </summary>
    [ToolParameter(Description = "Max retries after initial failure (default 2, max 5). Total attempts = MaxRetries + 1.",
        Required = false)]
    public int MaxRetries { get; init; } = 2;

    /// <summary>
    /// Tools available to each attempt's sub-agent.
    /// </summary>
    [ToolParameter(Description = "Tools to grant each attempt's sub-agent. " +
        "Defaults to: fs_read, fs_glob, fs_grep, fs_list, bash_run, bash_logs. " +
        "Write tools and channel tools are never granted.",
        Required = false)]
    public IReadOnlyList<string>? Tools { get; init; }

    /// <summary>
    /// Working directory override for each attempt's sub-agent.
    /// </summary>
    [ToolParameter(Description = "Working directory override (defaults to parent workspace root)",
        Required = false)]
    public string? WorkDir { get; init; }

    /// <summary>
    /// Maximum tool-call iterations per attempt.
    /// </summary>
    [ToolParameter(Description = "Max iterations per attempt (default 20, max 50)",
        Required = false)]
    public int MaxIterationsPerAttempt { get; init; } = 20;
}
