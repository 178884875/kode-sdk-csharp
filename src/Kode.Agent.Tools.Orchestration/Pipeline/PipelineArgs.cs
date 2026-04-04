using Kode.Agent.Sdk.Tools;

namespace Kode.Agent.Tools.Orchestration;

/// <summary>
/// Arguments for the <c>pipeline</c> tool.
/// </summary>
[GenerateToolSchema]
public class PipelineArgs
{
    /// <summary>
    /// The ordered list of pipeline stages to execute.
    /// </summary>
    [ToolParameter(Description = "Pipeline stages to execute in order. Each stage runs in an isolated sub-agent.")]
    public required IReadOnlyList<PipelineStage> Stages { get; init; }

    /// <summary>
    /// Whether to abort the pipeline when a stage fails.
    /// </summary>
    [ToolParameter(Description = "Stop pipeline execution if any stage fails (default true). " +
        "Set to false to collect results from all stages regardless of failures.",
        Required = false)]
    public bool StopOnFailure { get; init; } = true;
}

/// <summary>
/// A single stage within a <see cref="PipelineArgs"/> pipeline.
/// </summary>
[GenerateToolSchema]
public class PipelineStage
{
    /// <summary>
    /// Human-readable label for this stage. Used in output and context handoff.
    /// </summary>
    [ToolParameter(Description = "Optional label for this stage (used in output and context passed to next stage)",
        Required = false)]
    public string? Name { get; init; }

    /// <summary>
    /// The task description for the sub-agent running this stage.
    /// </summary>
    [ToolParameter(Description = "Task description for this stage's sub-agent")]
    public required string Task { get; init; }

    /// <summary>
    /// Tools available to this stage's sub-agent.
    /// </summary>
    [ToolParameter(Description = "Tools to grant this stage's sub-agent. " +
        "Defaults to: fs_read, fs_glob, fs_grep, fs_list, bash_run, bash_logs. " +
        "Write tools and channel tools are never granted.",
        Required = false)]
    public IReadOnlyList<string>? Tools { get; init; }

    /// <summary>
    /// Working directory override for this stage's sub-agent.
    /// </summary>
    [ToolParameter(Description = "Working directory override for this stage (defaults to parent workspace root)",
        Required = false)]
    public string? WorkDir { get; init; }

    /// <summary>
    /// Maximum tool-call iterations for this stage.
    /// </summary>
    [ToolParameter(Description = "Max iterations for this stage's sub-agent (default 20, max 50)",
        Required = false)]
    public int MaxIterations { get; init; } = 20;
}
