using Kode.Agent.Sdk.Tools;

namespace Kode.Agent.Tools.Orchestration;

/// <summary>
/// Arguments for the <c>isolate_task</c> tool.
/// </summary>
[GenerateToolSchema]
public class IsolateTaskArgs
{
    /// <summary>
    /// The investigation task description. Be specific about what you need to know.
    /// </summary>
    [ToolParameter(Description =
        "Detailed description of the investigation task and what information to return")]
    public required string Task { get; init; }

    /// <summary>
    /// Override the working directory for the sub-agent.
    /// </summary>
    [ToolParameter(Description =
        "Working directory for the sub-agent (e.g. /path/to/project). " +
        "Defaults to the current workspace root.",
        Required = false)]
    public string? WorkDir { get; init; }

    /// <summary>
    /// Subset of tools to grant the sub-agent.
    /// </summary>
    [ToolParameter(Description =
        "Tools to grant the sub-agent. Defaults to: fs_read, fs_glob, fs_grep, fs_list, bash_run, bash_logs. " +
        "Write tools and channel tools are never granted regardless of this list.",
        Required = false)]
    public IReadOnlyList<string>? Tools { get; init; }

    /// <summary>
    /// Maximum iterations the sub-agent may run. Clamped to [1, 50].
    /// </summary>
    [ToolParameter(Description = "Maximum tool-call iterations (default 12, max 50)",
        Required = false)]
    public int MaxIterations { get; init; } = 12;

    /// <summary>
    /// Maximum context tokens for the sub-agent. Increase for tasks that read many large files.
    /// </summary>
    [ToolParameter(Description =
        "Max context tokens for the sub-agent (default 80000). " +
        "Increase to 120000–160000 when the task reads many large files.",
        Required = false)]
    public int MaxContextTokens { get; init; } = 80_000;

    [ToolParameter(Description =
        "How to determine the iteration and context budget. " +
        "Fixed (default): use MaxIterations and MaxContextTokens as-is. " +
        "Auto: run a quick complexity analysis first and set both values automatically.",
        Required = false)]
    public MaxIterationsMode MaxIterationsMode { get; init; } = MaxIterationsMode.Fixed;
}
