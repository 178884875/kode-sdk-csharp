using Kode.Agent.Sdk.Tools;

namespace Kode.Agent.Tools.Orchestration;

/// <summary>
/// Arguments for the <c>parallel_research</c> tool.
/// </summary>
[GenerateToolSchema]
public class ParallelResearchArgs
{
    /// <summary>
    /// The research tasks to run in parallel.
    /// </summary>
    [ToolParameter(Description = "Research tasks to execute in parallel. Each runs in an isolated sub-agent.")]
    public required IReadOnlyList<ResearchTask> Tasks { get; init; }

    /// <summary>
    /// Maximum number of sub-agents running concurrently. 0 means unlimited.
    /// </summary>
    [ToolParameter(Description = "Max concurrent sub-agents (0 = unlimited, default 0). " +
        "Set to a lower value to avoid API rate limits.",
        Required = false)]
    public int MaxConcurrency { get; init; } = 0;

    /// <summary>
    /// When true, cancel all remaining tasks as soon as the first one fails.
    /// </summary>
    [ToolParameter(Description = "Stop all tasks immediately if any one fails (default false — collect all results)",
        Required = false)]
    public bool FailFast { get; init; } = false;
}

/// <summary>
/// A single research task within a <see cref="ParallelResearchArgs"/> batch.
/// </summary>
[GenerateToolSchema]
public class ResearchTask
{
    /// <summary>
    /// Human-readable label used in the result output.
    /// </summary>
    [ToolParameter(Description = "Label for this research task (used in output)",
        Required = false)]
    public string? Name { get; init; }

    /// <summary>
    /// The task description for the sub-agent.
    /// </summary>
    [ToolParameter(Description = "Task description for this sub-agent")]
    public required string Task { get; init; }

    /// <summary>
    /// Tools available to this sub-agent.
    /// </summary>
    [ToolParameter(Description = "Tools to grant this sub-agent. " +
        "Defaults to: fs_read, fs_glob, fs_grep, fs_list, bash_run, bash_logs. " +
        "Write tools and channel tools are never granted.",
        Required = false)]
    public IReadOnlyList<string>? Tools { get; init; }

    /// <summary>
    /// Working directory override for this sub-agent.
    /// </summary>
    [ToolParameter(Description = "Working directory override (defaults to parent workspace root)",
        Required = false)]
    public string? WorkDir { get; init; }

    /// <summary>
    /// Maximum tool-call iterations for this sub-agent.
    /// </summary>
    [ToolParameter(Description = "Max iterations for this sub-agent (default 12, max 50)",
        Required = false)]
    public int MaxIterations { get; init; } = 12;

    /// <summary>
    /// Maximum context tokens for this sub-agent.
    /// </summary>
    [ToolParameter(Description =
        "Max context tokens for this sub-agent (default 80000). " +
        "Increase to 120000–160000 when the task reads many large files.",
        Required = false)]
    public int MaxContextTokens { get; init; } = 80_000;

    [ToolParameter(Description =
        "Fixed: use MaxIterations/MaxContextTokens as-is. " +
        "Auto: estimate complexity from the task and set both values automatically.",
        Required = false)]
    public MaxIterationsMode MaxIterationsMode { get; init; } = MaxIterationsMode.Fixed;
}
