using Kode.Agent.Sdk.Tools;

namespace Kode.Agent.Tools.Orchestration;

[GenerateToolSchema]
public class MapReduceArgs
{
    [ToolParameter(Description = "List of items to process. Each item (or chunk) is processed by a separate sub-agent.")]
    public required IReadOnlyList<string> Items { get; init; }

    [ToolParameter(Description =
        "Task template for map sub-agents. Use {item} as placeholder for the current item/chunk content.")]
    public required string MapTask { get; init; }

    [ToolParameter(Description =
        "Task for the reduce sub-agent. All map summaries are prepended as context before this task.")]
    public required string ReduceTask { get; init; }

    [ToolParameter(Description = "Number of items per map sub-agent (default 1).", Required = false)]
    public int ChunkSize { get; init; } = 1;

    [ToolParameter(Description = "Max concurrent map sub-agents (0 = unlimited).", Required = false)]
    public int MaxConcurrency { get; init; } = 0;

    [ToolParameter(Description = "Tools for map sub-agents. Defaults to read-only tools.", Required = false)]
    public IReadOnlyList<string>? MapTools { get; init; }

    [ToolParameter(Description = "Max iterations per map sub-agent (default 10, max 50).", Required = false)]
    public int MapMaxIterations { get; init; } = 10;

    [ToolParameter(Description = "Max iterations for the reduce sub-agent (default 12, max 50).", Required = false)]
    public int ReduceMaxIterations { get; init; } = 12;

    [ToolParameter(Description =
        "Max context tokens per map sub-agent (default 80000). " +
        "Increase when individual items/chunks are large.",
        Required = false)]
    public int MapMaxContextTokens { get; init; } = 80_000;

    [ToolParameter(Description =
        "Max context tokens for the reduce sub-agent (default 80000). " +
        "Increase when there are many map results to aggregate.",
        Required = false)]
    public int ReduceMaxContextTokens { get; init; } = 80_000;

    [ToolParameter(Description =
        "Fixed: use MapMaxIterations/MapMaxContextTokens as-is for map agents. " +
        "Auto: estimate each chunk's complexity automatically.",
        Required = false)]
    public MaxIterationsMode MapMaxIterationsMode { get; init; } = MaxIterationsMode.Fixed;

    [ToolParameter(Description =
        "Fixed: use ReduceMaxIterations/ReduceMaxContextTokens as-is for the reduce agent. " +
        "Auto: estimate reduce complexity automatically.",
        Required = false)]
    public MaxIterationsMode ReduceMaxIterationsMode { get; init; } = MaxIterationsMode.Fixed;
}
