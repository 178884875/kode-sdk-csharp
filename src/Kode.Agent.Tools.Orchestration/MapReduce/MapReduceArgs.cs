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

    [ToolParameter(Description = "Max iterations for the reduce sub-agent (default 20, max 50).", Required = false)]
    public int ReduceMaxIterations { get; init; } = 20;
}
